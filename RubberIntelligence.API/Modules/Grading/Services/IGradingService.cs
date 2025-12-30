using RubberIntelligence.API.Modules.Grading.DTOs;

namespace RubberIntelligence.API.Modules.Grading.Services
{
    public interface IGradingService
    {
        Task<GradingResponse> AnalyzeImageAsync(IFormFile image);
    }
}
