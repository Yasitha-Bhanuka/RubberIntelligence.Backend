namespace RubberIntelligence.API.Modules.Dpp.DTOs
{
    public class ClassificationResultDto
    {
        public string FileName { get; set; } = string.Empty;
        public string Classification { get; set; } = string.Empty; // "CONFIDENTIAL" or "NON_CONFIDENTIAL"
        public double ConfidenceScore { get; set; }
        public string ConfidenceLevel { get; set; } = string.Empty; // "High", "Medium", "Low"
        public string SystemAction { get; set; } = string.Empty; // "ENCRYPT + RESTRICT" or "ALLOW VIEWING"
        public string Explanation { get; set; } = string.Empty;
        public List<string> InfluentialKeywords { get; set; } = new();
        public bool IsEncrypted { get; set; }
        public string? ExtractedText { get; set; } // Optional: return OCR text for debugging/verification
    }
}
