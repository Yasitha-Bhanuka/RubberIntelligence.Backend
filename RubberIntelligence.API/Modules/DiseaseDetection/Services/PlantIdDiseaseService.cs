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
    public class PlantIdDiseaseService : ILeafDiseaseService
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

            var topSuggestion = suggestions[0];
            var diseaseName = topSuggestion.Name ?? "Unknown Disease";
            var probability = topSuggestion.Probability ?? 0;

            // Determine severity based on confidence
            string severity = probability switch
            {
                > 0.8 => "High",
                > 0.5 => "Medium",
                _ => "Low"
            };

            // Build remedy from disease description or provide a default
            var remedy = GetDiseaseRemedy(diseaseName);

            _logger.LogInformation("[PlantId] Disease detected: {Disease} ({Prob:P1})", diseaseName, probability);

            return new PredictionResponse
            {
                Label = diseaseName,
                Confidence = probability,
                Severity = severity,
                Remedy = remedy
            };
        }

        private static string GetDiseaseRemedy(string diseaseName)
        {
            // Provide specific remedies for common rubber tree diseases
            // The API returns standardized disease names
            var lowerName = diseaseName.ToLowerInvariant();

            if (lowerName.Contains("anthracnose"))
                return "Prune infected parts. Apply copper-based fungicides (Bordeaux mixture). Improve air circulation around trees.";
            if (lowerName.Contains("powdery mildew"))
                return "Apply sulfur-based dusts or wettable sulfur sprays. Ensure good air circulation. Remove severely affected leaves.";
            if (lowerName.Contains("leaf spot") || lowerName.Contains("corynespora"))
                return "Remove fallen leaves. Apply Mancozeb or Carbendazim fungicide. Avoid overhead watering.";
            if (lowerName.Contains("blight"))
                return "Remove and destroy infected plant parts. Apply appropriate fungicides. Improve drainage.";
            if (lowerName.Contains("rust"))
                return "Apply fungicides containing triazole. Remove infected leaves. Improve air circulation.";
            if (lowerName.Contains("water") && (lowerName.Contains("deficiency") || lowerName.Contains("stress")))
                return "Ensure adequate and regular watering. Check soil drainage. Apply mulch to retain moisture.";
            if (lowerName.Contains("nutrient") || lowerName.Contains("deficiency"))
                return "Apply balanced fertilizer (NPK). Check soil pH. Consider foliar feeding for quick response.";
            if (lowerName.Contains("healthy") || lowerName.Contains("abiotic"))
                return "No disease detected. Monitor for environmental stresses and maintain regular care.";

            // Generic remedy for unrecognized conditions
            return $"Condition '{diseaseName}' detected. Consult a rubber plantation expert or agricultural extension officer for specific treatment recommendations.";
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
