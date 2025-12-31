using RubberIntelligence.API.Modules.PriceForecasting.DTOs;

namespace RubberIntelligence.API.Modules.PriceForecasting.Services
{
    public interface IPriceForecastingService
    {
        Task<PricePredictionResponse> PredictPriceAsync(PricePredictionRequest request);
        Task<IEnumerable<PriceHistoryItem>> GetPriceHistoryAsync();
    }

    public class PriceHistoryItem
    {
        public DateTime Date { get; set; }
        public double Price { get; set; }
        public string Grade { get; set; }
    }
}
