using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class CompositeDiseaseService : IDiseaseDetectionService
    {
        private readonly OnnxLeafDiseaseService _leafService;
        private readonly OnnxPestDetectionService _pestService;
        private readonly OnnxWeedDetectionService _weedService;
        private readonly IImageValidationService _validationService;

        public CompositeDiseaseService(
            OnnxLeafDiseaseService leafService, 
            OnnxPestDetectionService pestService, 
            OnnxWeedDetectionService weedService,
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

            // 3. Return the result directly.
            // ONNX models output trained class labels exactly, so we no longer
            // need to map free-form external API labels through AllowedClasses.cs.
            return result;
        }
    }
}

