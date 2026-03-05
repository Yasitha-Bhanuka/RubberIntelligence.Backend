namespace RubberIntelligence.API.Modules.Marketplace.DTOs
{
    /// <summary>
    /// Returned by POST /api/Marketplace/transactions/{id}/qir after a successful upload.
    /// Shape mirrors InvoiceUploadResponseDto so ClassificationResultScreen is reusable.
    /// dppId here is the transactionId.
    /// </summary>
    public class QirUploadResponseDto
    {
        /// <summary>Transaction ID — plays the role of dppId on the frontend.</summary>
        public string DppId { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
        public int FieldsExtracted { get; set; }

        public List<QirFieldSummaryDto> Fields { get; set; } = new();
        public QirClassificationDto Classification { get; set; } = new();

        public string[] SupportedFormats { get; set; } =
            ["image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf"];
    }

    public class QirFieldSummaryDto
    {
        public string  FieldName       { get; set; } = string.Empty;
        public bool    IsConfidential  { get; set; }
        public double  ConfidenceScore { get; set; }
        public bool    HasValue        { get; set; }

        /// <summary>Plain text for public fields; null for confidential fields.</summary>
        public string? ExtractedValue  { get; set; }
    }

    public class QirClassificationDto
    {
        public string       Classification         { get; set; } = string.Empty;
        public double       ConfidenceScore        { get; set; }
        public string       ConfidenceLevel        { get; set; } = string.Empty;
        public string       SystemAction           { get; set; } = string.Empty;
        public string       Explanation            { get; set; } = string.Empty;
        public List<string> InfluentialKeywords    { get; set; } = new();
        public int          GeminiExtractedCount   { get; set; }
        public int          PublicFieldCount       { get; set; }
        public int          ConfidentialFieldCount { get; set; }
    }

    /// <summary>
    /// Returned by GET /api/Marketplace/transactions/{id}/qir-fields.
    /// Decrypted QIR field with confidentiality indicator.
    /// </summary>
    public class QirFieldDecryptedDto
    {
        public string  FieldName      { get; set; } = string.Empty;
        public string? Value          { get; set; }
        public bool    IsConfidential { get; set; }
    }
}
