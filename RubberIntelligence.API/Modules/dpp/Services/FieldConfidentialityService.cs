namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// Rule-based field-level confidentiality classification.
    /// Classifies each field individually based on its name.
    /// No ML, no ONNX — simple keyword matching.
    /// </summary>
    public class FieldConfidentialityService
    {
        // Field names that indicate confidential data
        private static readonly string[] ConfidentialKeywords = new[]
        {
            "price", "total", "bank", "account", "payment"
        };

        /// <summary>
        /// Classifies a single field as confidential or non-confidential
        /// based on its field name.
        /// </summary>
        public FieldClassificationResult Classify(string fieldName, string value)
        {
            var lowerName = fieldName.ToLowerInvariant();

            // Check if the field name contains any confidential keyword
            bool isConfidential = ConfidentialKeywords.Any(keyword => lowerName.Contains(keyword));

            double confidenceScore = isConfidential ? 0.9 : 0.8;

            bool manualReviewRequired = confidenceScore < 0.7;

            return new FieldClassificationResult
            {
                IsConfidential = isConfidential,
                ConfidenceScore = confidenceScore,
                ManualReviewRequired = manualReviewRequired
            };
        }
    }

    /// <summary>
    /// Result of classifying a single field.
    /// </summary>
    public class FieldClassificationResult
    {
        public bool IsConfidential { get; set; }
        public double ConfidenceScore { get; set; }
        public bool ManualReviewRequired { get; set; }
    }
}
