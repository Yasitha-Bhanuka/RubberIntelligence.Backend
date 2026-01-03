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

        public GeminiOcrService(HttpClient httpClient, IConfiguration config, ILogger<GeminiOcrService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY") 
                      ?? config["GoogleApiKey"] 
                      ?? throw new ArgumentNullException("GoogleApiKey not found in configuration");
        }

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

                // Use Gemini 1.5 Flash for speed and cost-effectiveness
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash-latest:generateContent?key={_apiKey}";

                var response = await _httpClient.PostAsync(url, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Gemini API Error: {error}");
                    return "";
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseJson);

                var extractedText = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? "";
                
                return extractedText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text with Gemini");
                return "";
            }
        }

        private async Task<string> ConvertToBase64(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            return Convert.ToBase64String(memoryStream.ToArray());
        }

        // Response DTOs
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
