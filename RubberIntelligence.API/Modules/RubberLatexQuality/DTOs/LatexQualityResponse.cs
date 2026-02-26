namespace RubberIntelligence.API.Modules.RubberLatexQuality.DTOs
{
    public class LatexQualityResponse
    {
        public string QualityGrade { get; set; } // e.g., "Excellent", "Good", "Fair", "Poor"
        public double Confidence { get; set; } // Model confidence score (0-1)
        public int QualityScore { get; set; } // Numeric score (0-100)
        public string Status { get; set; } // "Pass", "Warning", "Fail"
        public string[] Recommendations { get; set; }
        public SensorReadings SensorReadings { get; set; }
    }

    public class SensorReadings
    {
        public double Temperature { get; set; }
        public double Turbidity { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("pH")]
        public double PH { get; set; }
    }
}
