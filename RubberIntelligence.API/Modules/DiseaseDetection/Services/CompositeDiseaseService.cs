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

            // 3. Restrict output to trained classes (centralized in AllowedClasses.cs)
            //    If the API label does not match a trained class, mark as rejected.
            //    This also prevents proximity alerts from firing (DiseaseController
            //    checks IsRejected before calling AlertService).
            var mappedLabel = AllowedClasses.MapLabel(result.Label, request.Type);
            if (mappedLabel == null)
            {
                return new PredictionResponse
                {
                    Label = "Unidentified",
                    Confidence = result.Confidence,
                    Severity = "N/A",
                    Remedy = $"The detected condition '{result.Label}' is outside the trained model boundary. " +
                             "Please capture a clearer image or consult an agricultural expert.",
                    IsRejected = true,
                    RejectionReason = $"'{result.Label}' does not match any trained class for {request.Type} detection."
                };
            }

            // Apply the mapped (normalized) label
            result.Label = mappedLabel;
            return result;
        }
    }
}

