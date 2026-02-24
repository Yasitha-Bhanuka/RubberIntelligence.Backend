using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class CompositeDiseaseService : IDiseaseDetectionService
    {
        private readonly PlantIdDiseaseService _leafService;
        private readonly InsectIdPestService _pestService;
        private readonly PlantNetWeedService _weedService;
        private readonly IImageValidationService _validationService;

        public CompositeDiseaseService(
            PlantIdDiseaseService leafService, 
            InsectIdPestService pestService, 
            PlantNetWeedService weedService,
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
            if (request.Type == DiseaseType.Pest)
            {
                return await _pestService.PredictAsync(request);
            }
            else if (request.Type == DiseaseType.Weed)
            {
                return await _weedService.PredictAsync(request);
            }
            // Default to Leaf Disease
            else
            {
                return await _leafService.PredictAsync(request);
            }
        }
    }
}

