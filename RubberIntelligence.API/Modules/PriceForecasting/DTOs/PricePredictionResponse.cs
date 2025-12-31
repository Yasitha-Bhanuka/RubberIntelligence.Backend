namespace RubberIntelligence.API.Modules.PriceForecasting.DTOs
{
    public class PricePredictionResponse
    {
        public float PredictedPriceLkr { get; set; }
        public string Currency { get; set; } = "LKR";
    }
}
