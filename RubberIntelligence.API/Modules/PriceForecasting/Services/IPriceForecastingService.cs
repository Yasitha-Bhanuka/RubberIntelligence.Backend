using RubberIntelligence.API.Modules.PriceForecasting.DTOs;

namespace RubberIntelligence.API.Modules.PriceForecasting.Services
{
    public interface IPriceForecastingService
    {
        Task<PricePredictionResponse> PredictPriceAsync(PricePredictionRequest request);
    }
}
