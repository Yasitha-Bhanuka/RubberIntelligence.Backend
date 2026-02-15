using System.Text.Json;
using System.Text.Json.Serialization;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;


namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class PlantNetWeedService : IDiseaseDetectionService
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

                // Iterate through suggestions to find a rubber-related weed
                foreach (var match in result.Results)
                {
                    var scientificName = match.Species?.ScientificNameWithoutAuthor ?? "";
                    var commonNames = match.Species?.CommonNames ?? new List<string>();
                    var score = match.Score;

                    // Only consider reasonable matches
                    if (score < 0.1) continue;

                    var mappedLabel = MapToRubberWeed(scientificName, commonNames);
                    if (mappedLabel != null)
                    {
                        var displayLabel = $"{mappedLabel} ({scientificName})";
                        return new PredictionResponse
                        {
                            Label = displayLabel,
                            Confidence = score,
                            Severity = "N/A",
                            Remedy = GetWeedRemedy(mappedLabel)
                        };
                    }
                }

                // No rubber-specific weed found
                var topMatch = result.Results[0];
                var topName = topMatch.Species?.CommonNames?.FirstOrDefault() ?? topMatch.Species?.ScientificNameWithoutAuthor ?? "Unknown";
                _logger.LogInformation("[WeedCheck] No rubber-specific weed found. Top match: {Top}", topName);

                return new PredictionResponse
                {
                    Label = "Non-Rubber Weed Detected",
                    Confidence = 0.0,
                    Severity = "N/A",
                    Remedy = "This plant is not recognized as a common weed in rubber plantations. Check if it is a cover crop or harmless vegetation.",
                    IsRejected = false
                };

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WeedCheck] Exception during integration.");
                throw;
            }
        }

        private static string? MapToRubberWeed(string scientificName, List<string> commonNames)
        {
            var combined = (scientificName + " " + string.Join(" ", commonNames)).ToLowerInvariant();

            // 1. Imperata (Cogon grass)
            string[] imperataKeywords = { "imperata", "cylindrica", "cogon", "lalang" };
            if (imperataKeywords.Any(k => combined.Contains(k))) return "Imperata (Cogon Grass)";

            // 2. Chromolaena (Siam weed)
            string[] chromolaenaKeywords = { "chromolaena", "odorata", "siam weed", "bitter bush", "eupatorium" };
            if (chromolaenaKeywords.Any(k => combined.Contains(k))) return "Chromolaena";

            // 3. Mikania (Mile-a-minute)
            string[] mikaniaKeywords = { "mikania", "micrantha", "mile-a-minute", "climbing hempweed" };
            if (mikaniaKeywords.Any(k => combined.Contains(k))) return "Mikania";

            // 4. Ageratum (Billygoat weed)
            string[] ageratumKeywords = { "ageratum", "conyzoides", "billygoat", "goat weed" };
            if (ageratumKeywords.Any(k => combined.Contains(k))) return "Ageratum";

            // 5. Axonopus (Carpet grass)
            string[] axonopusKeywords = { "axonopus", "compressus", "carpet grass", "broadleaf carpet grass" };
            if (axonopusKeywords.Any(k => combined.Contains(k))) return "Axonopus";

            // 6. Panicum (Guinea grass)
            string[] panicumKeywords = { "panicum", "maximum", "guinea grass", "megathyrsus" };
            if (panicumKeywords.Any(k => combined.Contains(k))) return "Panicum";

            // 7. Mimosa (Sensitive plant)
            string[] mimosaKeywords = { "mimosa", "pudica", "sensitive plant", "sleepy plant" };
            if (mimosaKeywords.Any(k => combined.Contains(k))) return "Mimosa";

            return null;
        }

        private static string GetWeedRemedy(string weedName)
        {
            if (weedName.Contains("Imperata"))
                return "Apply glyphosate herbicide. Manual slashing is ineffective as it regenerates from rhizomes. Ensure complete coverage.";
            if (weedName.Contains("Chromolaena"))
                return "Uproot manualy before flowering. Apply 2,4-D or triclopyr herbicides. Biological control agents (Cecidochares connexa) are also used.";
            if (weedName.Contains("Mikania"))
                return "Hand weeding or slashing. Apply herbicides like 2,4-D amine or glyphosate. Prevent it from climbing up young rubber trees.";
            if (weedName.Contains("Ageratum"))
                return "Manual weeding. Apply pre-emergence herbicides. Maintain ground cover crops to suppress growth.";
            if (weedName.Contains("Axonopus"))
                return "Generally less competitive. Can be managed by selective slashing or mild herbicides if overriding cover crops.";
            if (weedName.Contains("Panicum"))
                return "Slash regularly. Apply glyphosate. This grass competes heavily for nutrients, so control is important in young plantations.";
            if (weedName.Contains("Mimosa"))
                return "Manual digging (wear gloves). Apply herbicides like Picloram or Triclopyr. Persistent seeds require long-term management.";

            return "Use general weed control methods: manual weeding, slashing, or appropriate herbicides.";
        }

        // Inner DTO classes for deserialization
        private class PlantNetResponse
        {
            [JsonPropertyName("results")]
            public List<PlantResult> Results { get; set; }
        }

        private class PlantResult
        {
            [JsonPropertyName("score")]
            public double Score { get; set; }
            [JsonPropertyName("species")]
            public PlantSpecies Species { get; set; }
        }

        private class PlantSpecies
        {
            [JsonPropertyName("scientificNameWithoutAuthor")]
            public string ScientificNameWithoutAuthor { get; set; }
            [JsonPropertyName("commonNames")]
            public List<string> CommonNames { get; set; }
        }
    }
}
