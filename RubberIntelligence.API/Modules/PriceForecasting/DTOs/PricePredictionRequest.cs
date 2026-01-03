namespace RubberIntelligence.API.Modules.PriceForecasting.DTOs
{
    public class PricePredictionRequest
    {
        public string RubberSheetGrade { get; set; } = string.Empty; // e.g., "RSS1"
        public float QuantityKg { get; set; }
        
        // Changed from numeric percentages to categorical levels
        public string MoistureLevel { get; set; } = "Normal"; // Options: Dry, Normal, Wet
        public string Cleanliness { get; set; } = "Clean"; // Options: Clean, Slight, Dirty

        public float VisualQualityScore { get; set; }
        public string District { get; set; } = string.Empty; // e.g., "Galle"
        public string MarketAvailability { get; set; } = string.Empty; // e.g., "Immediately"
    }
}
