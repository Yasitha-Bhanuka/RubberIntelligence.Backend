using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    /// <summary>
    /// Uses the Insect.id API (Kindwise) to identify pests from images.
    /// Replaces the custom-trained ONNX pest detection model with a
    /// pretrained model covering thousands of invertebrate species.
    /// API docs: https://insect.kindwise.com/docs
    /// </summary>
    public class InsectIdPestService : IDiseaseDetectionService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly ILogger<InsectIdPestService> _logger;

        private const string ApiBaseUrl = "https://insect.kindwise.com/api/v1/identification";

        public InsectIdPestService(HttpClient httpClient, IConfiguration config, ILogger<InsectIdPestService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = Environment.GetEnvironmentVariable("INSECTID_API_KEY") ?? config["InsectId:ApiKey"];
        }

        public async Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("[InsectId] API Key missing. Returning mock result.");
                return new PredictionResponse
                {
                    Label = "Unknown Pest (No API Key)",
                    Confidence = 0.0,
                    Remedy = "Please configure INSECTID_API_KEY in backend .env to enable real detection.",
                    Severity = "Low"
                };
            }

            try
            {
                _logger.LogInformation("[InsectId] Starting pest identification via Insect.id API...");

                // 1. Convert image to base64
                using var stream = request.Image.OpenReadStream();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var base64Image = Convert.ToBase64String(memoryStream.ToArray());

                // 2. Build request body
                var requestBody = new
                {
                    images = new[] { base64Image }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // 3. Send request with Api-Key header
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl);
                httpRequest.Headers.Add("Api-Key", _apiKey);
                httpRequest.Content = content;

                var response = await _httpClient.SendAsync(httpRequest);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[InsectId] API Error: {StatusCode} - {Error}", response.StatusCode, error);
                    throw new HttpRequestException($"Insect.id API returned {response.StatusCode}: {error}");
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("[InsectId] Raw response: {Response}", responseJson);

                var result = JsonSerializer.Deserialize<InsectIdResponse>(responseJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // 4. Parse identification result
                return ParseIdentificationResult(result);
            }
            catch (Exception ex) when (ex is not HttpRequestException)
            {
                _logger.LogError(ex, "[InsectId] Exception during pest identification.");
                throw;
            }
        }

        private PredictionResponse ParseIdentificationResult(InsectIdResponse? response)
        {
            var classificationResult = response?.Result?.Classification;
            var suggestions = classificationResult?.Suggestions;

            // Check if the image actually contains an insect
            var isInsect = response?.Result?.IsInsect;
            if (isInsect != null && isInsect.Binary == false)
            {
                _logger.LogWarning("[InsectId] Image does not appear to contain an insect (probability: {Prob:P1})", isInsect.Probability);
                return new PredictionResponse
                {
                    Label = "No Pest Detected",
                    Confidence = 1.0 - (isInsect.Probability ?? 0),
                    Severity = "None",
                    Remedy = "The image does not appear to contain an insect or pest. Try capturing a clearer photo of the pest."
                };
            }

            if (suggestions == null || suggestions.Count == 0)
            {
                _logger.LogWarning("[InsectId] No identification suggestions returned.");
                return new PredictionResponse
                {
                    Label = "Unidentified Pest",
                    Confidence = 0.0,
                    Remedy = "Could not identify the pest. Try a closer, clearer photo.",
                    Severity = "Low"
                };
            }

            var topSuggestion = suggestions[0];
            var pestName = topSuggestion.Name ?? "Unknown Pest";
            var probability = topSuggestion.Probability ?? 0;

            // Determine severity based on confidence
            string severity = probability switch
            {
                > 0.8 => "High",
                > 0.5 => "Medium",
                _ => "Low"
            };

            // Get pest-specific remedy
            var remedy = GetPestRemedy(pestName);

            _logger.LogInformation("[InsectId] Pest identified: {Pest} ({Prob:P1})", pestName, probability);

            return new PredictionResponse
            {
                Label = pestName,
                Confidence = probability,
                Severity = severity,
                Remedy = remedy
            };
        }

        private static string GetPestRemedy(string pestName)
        {
            var lowerName = pestName.ToLowerInvariant();

            if (lowerName.Contains("aphid"))
                return "Use neem oil or insecticidal soap. Encourage natural predators like ladybugs. Spray with water to dislodge.";
            if (lowerName.Contains("mite") || lowerName.Contains("spider"))
                return "Apply miticide or neem oil spray. Increase humidity around plants. Remove heavily infested leaves.";
            if (lowerName.Contains("whitefly"))
                return "Use yellow sticky traps. Apply neem oil or insecticidal soap. Introduce natural predators like Encarsia.";
            if (lowerName.Contains("beetle") || lowerName.Contains("weevil"))
                return "Pick off visible beetles manually. Apply neem oil or pyrethrin-based insecticides if infestation is severe.";
            if (lowerName.Contains("caterpillar") || lowerName.Contains("looper") || lowerName.Contains("moth") || lowerName.Contains("larva"))
                return "Remove manually. Apply Bacillus thuringiensis (Bt) biological insecticide. Check under leaves regularly.";
            if (lowerName.Contains("slug") || lowerName.Contains("snail"))
                return "Remove manually. Use diatomaceous earth or organic bait around plants. Water in the morning to reduce moisture.";
            if (lowerName.Contains("grasshopper") || lowerName.Contains("cricket"))
                return "Keep area free of debris. Use neem oil sprays. Consider bird perches to attract natural predators.";
            if (lowerName.Contains("thrip"))
                return "Use blue sticky traps. Apply spinosad or neem oil. Remove and destroy infested plant material.";
            if (lowerName.Contains("scale"))
                return "Scrub off with rubbing alcohol on cotton swab. Apply horticultural oil. For severe infestations, use systemic insecticide.";
            if (lowerName.Contains("mealy") || lowerName.Contains("mealybug"))
                return "Dab with rubbing alcohol. Spray with neem oil or insecticidal soap. Isolate affected plants.";
            if (lowerName.Contains("ant"))
                return "Use bait stations with borax. Apply diatomaceous earth around base. Manage aphid populations (ants farm aphids).";
            if (lowerName.Contains("fly") || lowerName.Contains("fruit fly"))
                return "Use pheromone traps. Destroy fallen infected fruit. Apply certified insecticides if necessary.";
            if (lowerName.Contains("cockroach"))
                return "Keep area clean. Use bait stations. Apply insecticides in crevices and hiding spots.";
            if (lowerName.Contains("termite"))
                return "This is a serious infestation. Contact a professional pest control service. Apply termiticide around affected areas.";

            // Generic remedy
            return $"Pest '{pestName}' identified. Apply general insecticide or consult an agriculture extension officer for specific treatment.";
        }

        // ── Insect.id API Response DTOs ─────────────────────────────────────

        private class InsectIdResponse
        {
            [JsonPropertyName("result")]
            public InsectResult? Result { get; set; }

            [JsonPropertyName("status")]
            public string? Status { get; set; }
        }

        private class InsectResult
        {
            [JsonPropertyName("classification")]
            public ClassificationResult? Classification { get; set; }

            [JsonPropertyName("is_insect")]
            public IsInsectPrediction? IsInsect { get; set; }
        }

        private class IsInsectPrediction
        {
            [JsonPropertyName("binary")]
            public bool? Binary { get; set; }

            [JsonPropertyName("probability")]
            public double? Probability { get; set; }
        }

        private class ClassificationResult
        {
            [JsonPropertyName("suggestions")]
            public List<InsectSuggestion>? Suggestions { get; set; }
        }

        private class InsectSuggestion
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("probability")]
            public double? Probability { get; set; }
        }
    }
}
