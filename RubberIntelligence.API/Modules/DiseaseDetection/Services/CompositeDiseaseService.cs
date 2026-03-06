using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class CompositeDiseaseService : IDiseaseDetectionService
    {
        private readonly ILeafDiseaseService _leafService;
        private readonly IPestDetectionService _pestService;
        private readonly IWeedDetectionService _weedService;
        private readonly IImageValidationService _validationService;

        public CompositeDiseaseService(
            ILeafDiseaseService leafService, 
            IPestDetectionService pestService, 
            IWeedDetectionService weedService,
            IImageValidationService validationService)
        {
            _leafService = leafService;
            _pestService = pestService;
            _weedService = weedService;
            _validationService = validationService;
        }

        public async Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
            // 1. Validate image quality + content before routing to AI model
            var validation = await _validationService.ValidateAsync(request);
            if (!validation.IsValid)
            {
                return new PredictionResponse
                {
                    Label = "Rejected",
                    Confidence = 0,
                    Severity = "N/A",
                    Remedy = "N/A",
                    IsRejected = true,
                    RejectionReason = validation.RejectReason
                };
            }

            // 2. Route to correct AI service based on type
            PredictionResponse result;
            if (request.Type == DiseaseType.Pest)
            {
                result = await _pestService.PredictAsync(request);
            }
            else if (request.Type == DiseaseType.Weed)
            {
                result = await _weedService.PredictAsync(request);
            }
            else // Default to Leaf Disease
            {
                result = await _leafService.PredictAsync(request);
            }

            // 3. Map to Allowed Classes if using External API
            // External APIs return free-form strings (e.g. "Bemisia tabaci").
            // We map these back to our recognized plantation classes 
            // (e.g. "Whitefly"). If it doesn't match, we reject it.
            // Weed Detection ALWAYS uses external API now.
            bool isExternalApi = request.Type == DiseaseType.Weed || !(_leafService is OnnxLeafDiseaseService);

            if (isExternalApi && !result.IsRejected)
            {
                var mappedLabel = AllowedClasses.MapLabel(result.Label, request.Type);
                if (mappedLabel != null)
                {
                    result.Label = mappedLabel; // e.g. "Bemisia" -> "Whitefly"
                }
                else
                {
                    // It's a disease/pest the system doesn't care about (e.g., Apple Scab, House Spider)
                    result.IsRejected = true;
                    result.RejectionReason = $"The detected condition/pest '{result.Label}' is not recognized as a standard rubber plantation threat.";
                    result.Label = "Unrecognized Domain";
                    result.Severity = "N/A";
                }
            }

            return result;
        }
    }
}

