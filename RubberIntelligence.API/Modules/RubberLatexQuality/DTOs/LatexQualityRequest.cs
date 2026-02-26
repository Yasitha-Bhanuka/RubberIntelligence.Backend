namespace RubberIntelligence.API.Modules.RubberLatexQuality.DTOs
{
    public class LatexQualityRequest
    {
        public double Temperature { get; set; }
        public double Turbidity { get; set; }
        public double PH { get; set; }
        public string? TestId { get; set; }
        public string? TesterName { get; set; }
        public DateTime? TestDate { get; set; }
    }
}
