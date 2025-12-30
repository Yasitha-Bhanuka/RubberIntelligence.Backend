using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public interface IDiseaseDetectionService
    {
        Task<PredictionResponse> PredictAsync(PredictionRequest request);
    }
}
