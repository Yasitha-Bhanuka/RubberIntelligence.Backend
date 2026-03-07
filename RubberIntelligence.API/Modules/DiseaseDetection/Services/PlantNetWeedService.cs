using System.Text.Json;
using System.Text.Json.Serialization;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;


namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class PlantNetWeedService : IWeedDetectionService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<PlantNetWeedService> _logger;

        public PlantNetWeedService(HttpClient httpClient, IConfiguration config, ILogger<PlantNetWeedService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = Environment.GetEnvironmentVariable("PLANTNET_API_KEY") ?? config["PlantNet:ApiKey"];
        }

        public async Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
             if (string.IsNullOrEmpty(_apiKey))
            {
                // Fallback / Mock behavior if no key is present (to avoid crashing app during testing)
                _logger.LogWarning("[WeedCheck] PlantNet API Key missing. Returning Mock Result.");
                return new PredictionResponse 
                { 
                    Label = "Unknown Weed (No API Key)", 
                    Confidence = 0.0, 
                    Remedy = "Please configure PLANTNET_API_KEY in backend .env to enable real detection.",
                    Severity = "Low"
                };
            }

            try 
            {
                using var content = new MultipartFormDataContent();
                
                // Read image stream
                using var stream = request.Image.OpenReadStream();
                using var streamContent = new StreamContent(stream);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.Image.ContentType);
                
                content.Add(streamContent, "images", request.Image.FileName);
                content.Add(new StringContent("auto"), "organs"); // organ=auto let API decide.

                var url = $"https://my-api.plantnet.org/v2/identify/all?api-key={_apiKey}&include-related-images=false&no-reject=false&lang=en";
                
                _logger.LogInformation("[WeedCheck] Calling PlantNet API...");
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"[WeedCheck] API Error: {response.StatusCode} - {err}");
                    throw new Exception("Weed Detection API failed. Please try again later.");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PlantNetResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Results == null || result.Results.Count == 0)
                {
                    return new PredictionResponse 
                    { 
                        Label = "Unidentified Plant", 
                        Confidence = 0.0, 
                        Remedy = "Could not identify this plant. Try a clearer photo.",
                        Severity = "Low"
                    };
                }

                // Get Top Result
                var bestMatch = result.Results[0];
                var speciesName = bestMatch.Species.ScientificNameWithoutAuthor;
                var commonName = bestMatch.Species.CommonNames?.FirstOrDefault() ?? speciesName;
                var score = bestMatch.Score;

                return new PredictionResponse
                {
                    Label = $"{commonName} ({speciesName})",
                    Confidence = score,
                    Severity = "N/A", // PlantNet doesn't give severity
                    Remedy = "If this is a weed, apply appropriate herbicide (Glyphosate or Paraquat) or manual weeding."
                };

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WeedCheck] Exception during integration.");
                throw;
            }
        }

        // Inner DTO classes for deserialization
        private class PlantNetResponse
        {
            public List<PlantResult> Results { get; set; }
        }

        private class PlantResult
        {
            public double Score { get; set; }
            public PlantSpecies Species { get; set; }
        }

        private class PlantSpecies
        {
            public string ScientificNameWithoutAuthor { get; set; }
            public List<string> CommonNames { get; set; }
        }
    }
}
