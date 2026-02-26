namespace RubberIntelligence.API.Modules.DiseaseDetection.DTOs
{
    public class ImageQualityResult
    {
        public bool IsAcceptable { get; set; }
        public string? RejectReason { get; set; }
        public double BlurScore { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class ContentVerificationResult
    {
        public bool IsContentValid { get; set; }
        public string? RejectReason { get; set; }
        public string? DetectedCategory { get; set; }
        public float TopConfidence { get; set; }
        public List<string> TopLabels { get; set; } = new();
    }

    public class ImageValidationResult
    {
        public bool IsValid { get; set; }
        public string? RejectReason { get; set; }
        public ImageQualityResult? QualityResult { get; set; }
        public ContentVerificationResult? ContentResult { get; set; }
    }
}
