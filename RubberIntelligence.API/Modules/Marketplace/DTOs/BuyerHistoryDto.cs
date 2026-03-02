namespace RubberIntelligence.API.Modules.Marketplace.DTOs
{
    public class BuyerHistoryDto
    {
        public string BuyerId { get; set; } = string.Empty;
        public int TotalLots { get; set; }
        public int Accepted { get; set; }
        public int Rejected { get; set; }
        public int ReInspections { get; set; }
        public double AverageQuality { get; set; }
        // High (>=80% DPP coverage), Medium (>=40%), Low (<40%)
        public string VerificationConsistency { get; set; } = "Low";
        public DateTime? LastActivityDate { get; set; }
    }
}
