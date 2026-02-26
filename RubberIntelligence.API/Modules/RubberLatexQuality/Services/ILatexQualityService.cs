using RubberIntelligence.API.Modules.RubberLatexQuality.DTOs;

namespace RubberIntelligence.API.Modules.RubberLatexQuality.Services
{
    public interface ILatexQualityService
    {
        Task<LatexQualityResponse> PredictQualityAsync(LatexQualityRequest request);
    }
}
