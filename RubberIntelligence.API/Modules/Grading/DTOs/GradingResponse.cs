namespace RubberIntelligence.API.Modules.Grading.DTOs
{
    public class GradingResponse
    {
        public string PredictedClass { get; set; } // e.g., "Good Quality"
        public double Confidence { get; set; }
        public string Severity { get; set; } // Low, Medium, High
        public string Suggestions { get; set; } // e.g., "Keep dry..."
    }
}
