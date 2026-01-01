using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class CompositeDiseaseService : IDiseaseDetectionService
    {
        private readonly OnnxLeafDiseaseService _leafService;
        private readonly OnnxPestDetectionService _pestService;
        private readonly PlantNetWeedService _weedService;

        public CompositeDiseaseService(OnnxLeafDiseaseService leafService, OnnxPestDetectionService pestService, PlantNetWeedService weedService)
        {
            _leafService = leafService;
            _pestService = pestService;
            _weedService = weedService;
        }

        public async Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
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
