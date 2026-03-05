namespace RubberIntelligence.API.Modules.Dpp.DTOs
{
    /// <summary>
    /// Returned by POST /api/Marketplace/transactions/{id}/invoice after a successful upload.
    /// Shape is intentionally identical to the DppUploadResponse so the React Native
    /// ClassificationResultScreen can be reused without modification by passing isInvoice=true.
    /// </summary>
    public class InvoiceUploadResponseDto
    {
        /// <summary>Transaction ID — plays the role of dppId on the frontend.</summary>
        public string DppId { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
        public int FieldsExtracted { get; set; }

        public List<InvoiceFieldSummaryDto> Fields { get; set; } = new();
        public InvoiceClassificationDto Classification { get; set; } = new();

        /// <summary>Mirrors DppUploadResponse.supportedFormats for frontend compatibility.</summary>
        public string[] SupportedFormats { get; set; } =
            ["image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf"];
    }

    public class InvoiceFieldSummaryDto
    {
        public string FieldName       { get; set; } = string.Empty;
        public bool   IsConfidential  { get; set; }
        public double ConfidenceScore { get; set; }
        public bool   HasValue        { get; set; }

        /// <summary>Plain text for public fields; null for confidential fields (never exposed).</summary>
        public string? ExtractedValue { get; set; }
    }

    public class InvoiceClassificationDto
    {
        public string       Classification      { get; set; } = string.Empty;
        public double       ConfidenceScore     { get; set; }
        public string       ConfidenceLevel     { get; set; } = string.Empty;
        public string       SystemAction        { get; set; } = string.Empty;
        public string       Explanation         { get; set; } = string.Empty;
        public List<string> InfluentialKeywords { get; set; } = new();
        public int          GeminiExtractedCount   { get; set; }
        public int          PublicFieldCount        { get; set; }
        public int          ConfidentialFieldCount  { get; set; }
    }
}

