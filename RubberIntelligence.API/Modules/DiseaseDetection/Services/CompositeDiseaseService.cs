using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class CompositeDiseaseService : IDiseaseDetectionService
    {
        private readonly OnnxLeafDiseaseService _leafService;
        private readonly OnnxPestDetectionService _pestService;

        public CompositeDiseaseService(OnnxLeafDiseaseService leafService, OnnxPestDetectionService pestService)
        {
            _leafService = leafService;
            _pestService = pestService;
        }

        public async Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
            if (request.Type == DiseaseType.Pest)
            {
                return await _pestService.PredictAsync(request);
            }
               // Default to Leaf Disease for now (Type 0 = Leaf Disease, Type 2 = Weed could also be mapped if needed)
            else
            {
                return await _leafService.PredictAsync(request);
            }
        }
    }
}
