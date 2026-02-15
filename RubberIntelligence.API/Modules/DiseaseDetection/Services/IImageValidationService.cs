using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public interface IImageValidationService
    {
        Task<ImageValidationResult> ValidateAsync(PredictionRequest request);
    }
}
