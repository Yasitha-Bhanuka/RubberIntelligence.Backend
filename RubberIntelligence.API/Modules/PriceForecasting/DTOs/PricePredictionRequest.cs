namespace RubberIntelligence.API.Modules.PriceForecasting.DTOs
{
    public class PricePredictionRequest
    {
        public string RubberSheetGrade { get; set; } = string.Empty; // e.g., "RSS1"
        public float QuantityKg { get; set; }
        public float MoistureContentPct { get; set; }
        public float DirtContentPct { get; set; }
        public float VisualQualityScore { get; set; }
        public string District { get; set; } = string.Empty; // e.g., "Galle"
        public string MarketAvailability { get; set; } = string.Empty; // e.g., "Immediately"
    }
}
