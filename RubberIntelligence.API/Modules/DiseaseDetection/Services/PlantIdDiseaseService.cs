using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    /// <summary>
    /// Uses the Plant.id API v3 health_assessment endpoint to detect
    /// leaf diseases on rubber plants. Replaces the custom-trained ONNX model
    /// with a pretrained model covering 548+ plant health conditions.
    /// API docs: https://plant.id/docs
    /// </summary>
    public class PlantIdDiseaseService : IDiseaseDetectionService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly ILogger<PlantIdDiseaseService> _logger;

        private const string ApiBaseUrl = "https://plant.id/api/v3/health_assessment";

        public PlantIdDiseaseService(HttpClient httpClient, IConfiguration config, ILogger<PlantIdDiseaseService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = Environment.GetEnvironmentVariable("PLANTID_API_KEY") ?? config["PlantId:ApiKey"];
        }

        public async Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("[PlantId] API Key missing. Returning mock result.");
                return new PredictionResponse
                {
                    Label = "Unknown Disease (No API Key)",
                    Confidence = 0.0,
                    Remedy = "Please configure PLANTID_API_KEY in backend .env to enable real detection.",
                    Severity = "Low"
                };
            }

            try
            {
                _logger.LogInformation("[PlantId] Starting leaf disease detection via Plant.id API...");

                // 1. Convert image to base64
                using var stream = request.Image.OpenReadStream();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var base64Image = Convert.ToBase64String(memoryStream.ToArray());

                // 2. Build request body
                var requestBody = new
                {
                    images = new[] { base64Image },
                    // health=only returns only health assessment (1 credit)
                    health = "only"
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
                    _logger.LogError("[PlantId] API Error: {StatusCode} - {Error}", response.StatusCode, error);
                    throw new HttpRequestException($"Plant.id API returned {response.StatusCode}: {error}");
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("[PlantId] Raw response: {Response}", responseJson);

                var result = JsonSerializer.Deserialize<PlantIdHealthResponse>(responseJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // 4. Parse health assessment result
                return ParseHealthAssessment(result);
            }
            catch (Exception ex) when (ex is not HttpRequestException)
            {
                _logger.LogError(ex, "[PlantId] Exception during leaf disease detection.");
                throw;
            }
        }

        private PredictionResponse ParseHealthAssessment(PlantIdHealthResponse? response)
        {
            var healthResult = response?.Result;

            if (healthResult == null)
            {
                _logger.LogWarning("[PlantId] No result in response.");
                return new PredictionResponse
                {
                    Label = "Unidentified",
                    Confidence = 0.0,
                    Remedy = "Could not analyze the image. Try a clearer photo.",
                    Severity = "Low"
                };
            }

            // Check if plant is healthy
            var isHealthy = healthResult.IsHealthy;
            if (isHealthy?.Binary == true && (isHealthy.Probability ?? 0) > 0.7)
            {
                _logger.LogInformation("[PlantId] Plant is healthy (probability: {Prob:P1})", isHealthy.Probability);
                return new PredictionResponse
                {
                    Label = "Healthy",
                    Confidence = isHealthy.Probability ?? 1.0,
                    Severity = "None",
                    Remedy = "No action needed. The plant appears healthy. Maintain regular fertilization and monitoring."
                };
            }

            // Get top disease suggestion
            var suggestions = healthResult.Disease?.Suggestions;
            if (suggestions == null || suggestions.Count == 0)
            {
                _logger.LogWarning("[PlantId] No disease suggestions returned.");
                return new PredictionResponse
                {
                    Label = "Unidentified Condition",
                    Confidence = 0.0,
                    Remedy = "Could not identify the condition. Try a closer, clearer photo of the affected area.",
                    Severity = "Low"
                };
            }

            // Iterate through suggestions to find a rubber-related disease
            foreach (var suggestion in suggestions)
            {
                var originalName = suggestion.Name ?? "";
                var probability = suggestion.Probability ?? 0;

                // Only consider reasonably confident predictions (e.g. > 20%) to avoid noise
                if (probability < 0.2) continue;

                var mappedLabel = MapToRubberLeafDisease(originalName);
                if (mappedLabel != null)
                {
                    // Found a rubber-specific disease
                    _logger.LogInformation("[PlantId] Mapped '{Original}' to '{Mapped}' ({Prob:P1})", originalName, mappedLabel, probability);
                    
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
                        Remedy = GetDiseaseRemedy(mappedLabel)
                    };
                }
            }

            // If we get here, no rubber-specific disease was found in high-confidence suggestions
            _logger.LogInformation("[PlantId] No rubber-specific disease found. Top suggestion was: {Top}", suggestions[0].Name);
            
            return new PredictionResponse
            {
                Label = "Non-Rubber Disease Detected",
                Confidence = 0.0,
                Severity = "N/A",
                Remedy = "This condition is not recognized as a common rubber plantation disease. It may be a non-rubber plant or a less common issue. Please consult an expert.",
                IsRejected = false // or true, depending on UX preference. keeping false to show the message.
            };
        }

        private static string? MapToRubberLeafDisease(string apiName)
        {
            var lowerName = apiName.ToLowerInvariant();

            // 1. Anthracnose_Type (Colletotrichum species)
            string[] anthracnoseKeywords = { 
                "anthracnose", "colletotrichum", "gloeosporioides", "acutatum", "glomerella", 
                "cliviae", "siamense", "boninense", "truncatum" 
            };
            if (anthracnoseKeywords.Any(k => lowerName.Contains(k))) return "Anthracnose_Type";

            // 2. Corynespora_Leaf_Fall (Corynespora cassiicola)
            string[] corynesporaKeywords = { "corynespora", "cassiicola" };
            if (corynesporaKeywords.Any(k => lowerName.Contains(k))) return "Corynespora_Leaf_Fall";

            // 3. Powdery_Mildew (Oidium heveae / Erysiphe)
            string[] powderyKeywords = { "powdery mildew", "oidium", "erysiphe", "microsphaera", "quercicola", "golovinomyces", "podosphaera" };
            if (powderyKeywords.Any(k => lowerName.Contains(k))) return "Powdery_Mildew";

            // 4. Phytophthora_Leaf_Blight (Abnormal Leaf Fall)
            string[] phytophthoraKeywords = { "phytophthora", "palmivora", "meadii", "botryosa", "citrophthora" };
            if (phytophthoraKeywords.Any(k => lowerName.Contains(k))) return "Phytophthora_Leaf_Blight";

            // 5. Leaf_Spot_Group (Various fungal spots)
            // Includes: Pestalotiopsis, Curvularia, Drechslera (Bird's Eye), Alternaria, Cercospora, Fusicoccum, Guignardia
            string[] leafSpotKeywords = { 
                "leaf spot", "pestalotiopsis", "neopestalotiopsis", "curvularia", "drechslera", 
                "alternaria", "cercospora", "fusicoccum", "guignardia", "lasiodiplodia", "verrucospora", "phyllosticta", "birds eye", "bird's eye"
            };
            if (leafSpotKeywords.Any(k => lowerName.Contains(k))) return "Leaf_Spot_Group";

            return null;
        }

        private static string GetDiseaseRemedy(string diseaseName)
        {
            return diseaseName switch
            {
                "Anthracnose_Type" => "Prune infected parts. Apply copper-based fungicides (Bordeaux mixture). Improve air circulation around trees.",
                "Corynespora_Leaf_Fall" => "Remove fallen leaves (sanitation). Apply Mancozeb or Carbendazim fungicide. Avoid overhead watering.",
                "Powdery_Mildew" => "Apply sulfur-based dusts or wettable sulfur sprays. Ensure good air circulation using thermal fogging if possible.",
                "Phytophthora_Leaf_Blight" => "Remove and destroy infected fruits/leaves. Apply fungicides containing metalaxyl or fosetyl-aluminium. Improve drainage.",
                "Leaf_Spot_Group" => "General leaf spot management: Remove infected leaves, apply broad-spectrum fungicides if severe, and maintain tree vigor.",
                "Healthy" => "No disease detected. Monitor for environmental stresses and maintain regular care.",
                _ => "Consult a rubber plantation expert for specific advice."
            };
        }

        // ── Plant.id API v3 Response DTOs ──────────────────────────────────────

        private class PlantIdHealthResponse
        {
            [JsonPropertyName("result")]
            public HealthResult? Result { get; set; }
        }

        private class HealthResult
        {
            [JsonPropertyName("is_healthy")]
            public IsHealthyPrediction? IsHealthy { get; set; }

            [JsonPropertyName("disease")]
            public DiseaseResult? Disease { get; set; }
        }

        private class IsHealthyPrediction
        {
            [JsonPropertyName("binary")]
            public bool? Binary { get; set; }

            [JsonPropertyName("probability")]
            public double? Probability { get; set; }
        }

        private class DiseaseResult
        {
            [JsonPropertyName("suggestions")]
            public List<DiseaseSuggestion>? Suggestions { get; set; }
        }

        private class DiseaseSuggestion
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
