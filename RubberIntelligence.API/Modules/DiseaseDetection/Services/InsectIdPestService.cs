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

            // Iterate through suggestions to find a rubber-related pest
            foreach (var suggestion in suggestions)
            {
                var originalName = suggestion.Name ?? "";
                var probability = suggestion.Probability ?? 0;

                // Only consider reasonably confident predictions (e.g. > 15%)
                if (probability < 0.15) continue;

                var mappedLabel = MapToRubberPest(originalName);
                if (mappedLabel != null)
                {
                    // Found a rubber-specific pest
                    _logger.LogInformation("[InsectId] Mapped '{Original}' to '{Mapped}' ({Prob:P1})", originalName, mappedLabel, probability);

                    var severity = probability switch
                    {
                        > 0.8 => "High",
                        > 0.5 => "Medium",
                        _ => "Low"
                    };

                    return new PredictionResponse
                    {
                        Label = mappedLabel,
                        Confidence = probability,
                        Severity = severity,
                        Remedy = GetPestRemedy(mappedLabel)
                    };
                }
            }

            // If we get here, no rubber-specific pest was found
            _logger.LogInformation("[InsectId] No rubber-specific pest found. Top suggestion was: {Top}", suggestions[0].Name);

            return new PredictionResponse
            {
                Label = "Non-Rubber Pest Detected",
                Confidence = 0.0,
                Severity = "N/A",
                Remedy = "This insect is not recognized as a common rubber plantation pest. It might be harmless or incidental. Monitor for damage.",
                IsRejected = false
            };
        }

        private static string? MapToRubberPest(string apiName)
        {
            var lowerName = apiName.ToLowerInvariant();

            // 1. Rubber_Leaf_Skeletonizer (Lepidoptera larvae / Moths)
            string[] skeletonizerKeywords = { 
                "skeletonizer", "moth", "larva", "caterpillar", "lepidoptera", "armyworm", "looper", "aettoneura"
            };
            if (skeletonizerKeywords.Any(k => lowerName.Contains(k))) return "Rubber_Leaf_Skeletonizer";

            // 2. Rubber_Leafhopper (Cicadellidae)
            string[] leafhopperKeywords = { "leafhopper", "jassid", "cicadellidae", "empoasca", "idioscopus" };
            if (leafhopperKeywords.Any(k => lowerName.Contains(k))) return "Rubber_Leafhopper";

            // 3. Red_Spider_Mite (Acari)
            string[] miteKeywords = { "mite", "spider mite", "tetranychus", "oligonychus", "brevipalpus", "acari", "tarsonemidae" };
            if (miteKeywords.Any(k => lowerName.Contains(k))) return "Red_Spider_Mite";

            // 4. Thrips (Thysanoptera)
            string[] thripsKeywords = { "thrip", "thysanoptera", "scirtothrips", "frankliniella", "stenchaetothrips" };
            if (thripsKeywords.Any(k => lowerName.Contains(k))) return "Thrips";

            // 5. Rubber_Mealybug (Pseudococcidae)
            string[] mealybugKeywords = { "mealy", "mealybug", "pseudococcidae", "ferrisia", "paracoccus", "planococcus", "nipaecoccus", "coccid" };
            if (mealybugKeywords.Any(k => lowerName.Contains(k))) return "Rubber_Mealybug";

            // 6. Weevil (Curculionidae)
            string[] weevilKeywords = { "weevil", "curculionidae", "hypomeces", "tanymecus", "myllocerus", "snout beetle" };
            if (weevilKeywords.Any(k => lowerName.Contains(k))) return "Weevil (specific species)";

            return null;
        }

        private static string GetPestRemedy(string pestName)
        {
            return pestName switch
            {
                "Rubber_Leaf_Skeletonizer" => "For moth larvae/caterpillars: Remove manually if minor. Apply Bacillus thuringiensis (Bt) or neem oil. Check under leaves for egg masses.",
                "Rubber_Leafhopper" => "Use yellow sticky traps. Apply neem oil or systemic insecticides if population is high. Maintain weed-free surroundings.",
                "Red_Spider_Mite" => "Apply wettable sulfur or specific miticides. Increase humidity/mist leaves in dry weather. Predatory mites can be introduced.",
                "Thrips" => "Use blue sticky traps. Apply spinosad or neem oil. Remove and destroy heavily infested plant parts.",
                "Rubber_Mealybug" => "Dab with rubbing alcohol or spray with insecticidal soap/neem oil. Encourage ladybugs and parasitic wasps.",
                "Weevil (specific species)" => "Collect adult weevils by shaking branches onto sheets. Apply soil insecticides for larvae if root damage is suspected.",
                _ => "Consult a rubber plantation expert for specific pest management."
            };
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
