using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    public class GeminiOcrService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<GeminiOcrService> _logger;

        // Prompt that instructs Gemini to return ONLY a JSON object with known fields
        private const string StructuredPrompt = """
            You are a document parser for rubber trade documents.
            Extract the following fields from the document image and return ONLY a valid JSON object.
            Do NOT include any explanation, markdown formatting, or code fences — just the raw JSON.

            Fields to extract (use null if not found):
            {
              "rubberGrade": "...",
              "quantity": "...",
              "pricePerKg": "...",
              "totalAmount": "...",
              "dispatchPort": "...",
              "origin": "..."
            }
            """;

        // Prompt for structured invoice field extraction — invoice-specific schema
        private const string InvoiceStructuredPrompt = """
            You are a document parser for commercial trade invoices.
            Extract the following fields from the invoice and return ONLY a valid JSON object.
            Do NOT include any explanation, markdown formatting, or code fences — just the raw JSON.

            Fields to extract (use null if not found):
            {
              "invoiceNumber": "...",
              "invoiceDate": "...",
              "dueDate": "...",
              "sellerName": "...",
              "buyerName": "...",
              "totalAmount": "...",
              "taxAmount": "...",
              "currency": "...",
              "paymentTerms": "...",
              "lotReference": "..."
            }
            """;

        public GeminiOcrService(HttpClient httpClient, IConfiguration config, ILogger<GeminiOcrService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                      ?? config["GoogleApiKey"]
                      ?? throw new ArgumentNullException("GoogleApiKey not found in configuration");
        }

        // ── DPP document: structured key-value extraction ──
        public async Task<Dictionary<string, string>> ExtractFieldsAsync(Stream imageStream, string mimeType)
            => await ExtractStructuredFieldsAsync(imageStream, mimeType, StructuredPrompt, "DPP document");

        // ── Invoice document: structured key-value extraction (invoice schema, File API for PDFs) ──
        public async Task<Dictionary<string, string>> ExtractInvoiceFieldsAsync(Stream invoiceStream, string mimeType)
            => await ExtractStructuredFieldsAsync(invoiceStream, mimeType, InvoiceStructuredPrompt, "invoice");

        // ── Core: shared structured extraction — uses File API for PDFs, inline_data for images ──
        private async Task<Dictionary<string, string>> ExtractStructuredFieldsAsync(
            Stream docStream, string mimeType, string prompt, string docLabel)
        {
            string? fileUri = null;
            try
            {
                object docPart;
                if (string.Equals(mimeType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // File API path: handles multi-page PDFs; Google processes server-side and deletes after 48h
                    fileUri = await UploadFileToGeminiAsync(docStream, mimeType, $"{docLabel}.pdf");
                    docPart = new { file_data = new { mime_type = mimeType, file_uri = fileUri } };
                }
                else
                {
                    // inline_data path for images (JPEG, PNG, WEBP, GIF)
                    var base64Image = await ConvertToBase64(docStream);
                    docPart = new { inline_data = new { mime_type = mimeType, data = base64Image } };
                }

                var requestBody = new
                {
                    contents = new[] { new { parts = new object[] { new { text = prompt }, docPart } } }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";
                var response = await _httpClient.PostAsync(url, httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[GeminiOCR] API error for {DocLabel}: {StatusCode}", docLabel, response.StatusCode);
                    throw new HttpRequestException($"Gemini API Error: {error}", null, response.StatusCode);
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseJson);
                var rawText = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? "";

                // Log length only — never log raw content (may contain PII / financial data)
                _logger.LogDebug("[GeminiOCR] {DocLabel} response: {Len} chars", docLabel, rawText.Length);

                if (string.IsNullOrWhiteSpace(rawText))
                    throw new InvalidOperationException($"Gemini returned an empty response for {docLabel}.");

                return ParseJsonFields(rawText);
            }
            catch (Exception ex) when (ex is not HttpRequestException)
            {
                _logger.LogError(ex, "[GeminiOCR] Error extracting structured fields for {DocLabel}", docLabel);
                throw;
            }
            finally
            {
                // Always clean up temporary Gemini file — fire-and-forget, errors logged internally
                if (fileUri is not null)
                    _ = DeleteGeminiFileAsync(fileUri);
            }
        }

        // ── EXISTING: Returns raw extracted text (kept for backward compatibility) ──
        public async Task<string> ExtractTextAsync(Stream imageStream, string mimeType)
        {
            try
            {
                var base64Image = await ConvertToBase64(imageStream);

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = "Extract all text from this image faithfully. Output only the raw extracted text." },
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = mimeType,
                                        data = base64Image
                                    }
                                }
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Gemini API Error: {StatusCode} - {Error}", response.StatusCode, error);
                    throw new HttpRequestException($"Gemini API Error: {error}", null, response.StatusCode);
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseJson);
                return geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? "";
            }
            catch (Exception ex) when (ex is not HttpRequestException)
            {
                _logger.LogError(ex, "Error extracting text with Gemini");
                throw;
            }
        }

        // ── Safely parses a JSON string into Dictionary<string, string> ──
        private Dictionary<string, string> ParseJsonFields(string rawText)
        {
            // Strip markdown code fences if Gemini adds them despite the prompt
            var cleaned = rawText.Trim();
            if (cleaned.StartsWith("```"))
            {
                var firstNewline = cleaned.IndexOf('\n');
                var lastFence   = cleaned.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                    cleaned = cleaned.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }

            try
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using var doc = JsonDocument.Parse(cleaned);

                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    // Only store non-null string values
                    var value = property.Value.ValueKind == JsonValueKind.Null
                        ? string.Empty
                        : property.Value.ToString();

                    result[property.Name] = value;
                }

                return result;
            }
            catch (JsonException ex)
            {
                // Log length only — raw text may contain PII / financial data; never embed in exception messages
                _logger.LogWarning("[GeminiOCR] JSON parse failed for response of length {Len}.", rawText.Length);
                throw new InvalidOperationException(
                    "Gemini response could not be parsed as JSON. Check application logs.", ex);
            }
        }

        // ── Gemini File API: upload document for multi-page PDF support ──
        private async Task<string> UploadFileToGeminiAsync(Stream fileStream, string mimeType, string displayName)
        {
            // Step 1: initiate a resumable upload session
            var metadata = JsonSerializer.Serialize(new { file = new { display_name = displayName } });
            var initRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/upload/v1beta/files?uploadType=resumable&key={_apiKey}");
            initRequest.Headers.Add("X-Goog-Upload-Protocol", "resumable");
            initRequest.Headers.Add("X-Goog-Upload-Command", "start");
            initRequest.Headers.Add("X-Goog-Upload-Header-Content-Type", mimeType);
            initRequest.Content = new StringContent(metadata, Encoding.UTF8, "application/json");

            var initResponse = await _httpClient.SendAsync(initRequest);
            if (!initResponse.IsSuccessStatusCode)
            {
                var err = await initResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Gemini File API init failed: {err}", null, initResponse.StatusCode);
            }

            var uploadUrl = initResponse.Headers.GetValues("X-Goog-Upload-URL").FirstOrDefault()
                ?? throw new InvalidOperationException("Gemini File API did not return an upload URL.");

            // Step 2: upload the file bytes in a single request
            using var ms = new MemoryStream();
            await fileStream.CopyToAsync(ms);
            var fileBytes = ms.ToArray();

            var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            uploadRequest.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
            uploadRequest.Headers.Add("X-Goog-Upload-Offset", "0");
            uploadRequest.Content = new ByteArrayContent(fileBytes);
            uploadRequest.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);

            var uploadResponse = await _httpClient.SendAsync(uploadRequest);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var err = await uploadResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Gemini File API upload failed: {err}", null, uploadResponse.StatusCode);
            }

            var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(uploadJson);
            var uri = doc.RootElement.GetProperty("file").GetProperty("uri").GetString()
                ?? throw new InvalidOperationException("Gemini File API response missing file URI.");

            _logger.LogDebug("[GeminiOCR] Uploaded temporary file to Gemini: {DisplayName}", displayName);
            return uri;
        }

        // ── Gemini File API: delete the temporary file after processing ──
        private async Task DeleteGeminiFileAsync(string fileUri)
        {
            try
            {
                // fileUri format: https://generativelanguage.googleapis.com/v1beta/files/{name}
                var fileName = fileUri.Split('/').Last();
                var deleteUrl = $"https://generativelanguage.googleapis.com/v1beta/files/{fileName}?key={_apiKey}";
                var resp = await _httpClient.DeleteAsync(deleteUrl);
                _logger.LogDebug("[GeminiOCR] Deleted temp file {FileName}: {StatusCode}", fileName, resp.StatusCode);
            }
            catch (Exception ex)
            {
                // Fire-and-forget — swallow; files auto-expire after 48 h on Google's servers
                _logger.LogWarning(ex, "[GeminiOCR] Failed to delete temporary Gemini file {FileUri}", fileUri);
            }
        }

        private async Task<string> ConvertToBase64(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return Convert.ToBase64String(memoryStream.ToArray());
        }

        // ── Internal Gemini response model ──
        private class GeminiResponse
        {
            [JsonPropertyName("candidates")]
            public List<Candidate>? Candidates { get; set; }
        }

        private class Candidate
        {
            [JsonPropertyName("content")]
            public Content? Content { get; set; }
        }

        private class Content
        {
            [JsonPropertyName("parts")]
            public List<Part>? Parts { get; set; }
        }

        private class Part
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }
        }
    }
}

