namespace RubberIntelligence.API.Modules.Marketplace.DTOs
{
    /// <summary>
    /// Represents a single invoice field returned by
    /// GET /api/Marketplace/transactions/{id}/invoice-fields.
    /// Confidential fields carry their decrypted plaintext value;
    /// non-confidential fields carry the value stored directly in the database.
    /// </summary>
    public class InvoiceFieldDecryptedDto
    {
        /// <summary>Machine-friendly field key, e.g. "invoiceNumber", "totalAmount".</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// Decrypted plaintext value.
        /// Null if the field had no value or decryption failed.
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// True when the field was classified as containing commercially sensitive data
        /// and was AES-256-CBC encrypted at upload time.
        /// </summary>
        public bool IsConfidential { get; set; }
    }
}
