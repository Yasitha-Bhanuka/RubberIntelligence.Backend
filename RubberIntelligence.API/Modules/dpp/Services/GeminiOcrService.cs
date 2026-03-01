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

        public GeminiOcrService(HttpClient httpClient, IConfiguration config, ILogger<GeminiOcrService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                      ?? config["GoogleApiKey"]
                      ?? throw new ArgumentNullException("GoogleApiKey not found in configuration");
        }

        // ── NEW: Returns structured key-value fields from the document image ──
        public async Task<Dictionary<string, string>> ExtractFieldsAsync(Stream imageStream, string mimeType)
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
                                new { text = StructuredPrompt },
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
                var rawText = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? "";

                return ParseJsonFields(rawText);
            }
            catch (Exception ex) when (ex is not HttpRequestException)
            {
                _logger.LogError(ex, "Error extracting structured fields with Gemini");
                throw;
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
                _logger.LogWarning(ex, "Gemini returned invalid JSON. Raw response: {Raw}", rawText);
                // Return empty dict instead of crashing — caller can decide what to do
                return new Dictionary<string, string>();
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

