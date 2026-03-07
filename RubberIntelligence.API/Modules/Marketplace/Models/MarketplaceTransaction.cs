using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Marketplace.Models
{
    public class MarketplaceTransaction
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string PostId { get; set; } = string.Empty;

        // Participants
        public string ExporterId { get; set; } = string.Empty;
        public string ExporterName { get; set; } = string.Empty;
        public string BuyerId { get; set; } = string.Empty;

        [BsonElement("status")]
        /// PendingInvoice → InvoiceUploaded → QirUploaded → Completed
        public string Status { get; set; } = "PendingInvoice";

        [BsonElement("offerPrice")]
        public decimal OfferPrice { get; set; }

        [BsonElement("lastUpdatedAt")]
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("secretRequestId")]
        public string? SecretRequestId { get; set; }

        // DPP & Invoice Data
        [BsonElement("dppInvoicePath")]
        public string? DppInvoicePath { get; set; }

        [BsonElement("dppClassification")]
        public string? DppClassification { get; set; }

        [BsonElement("encryptionMetadata")]
        public string? EncryptionMetadata { get; set; } // JSON: { "IV": "...", "EncryptedKey": "..." }

        [BsonElement("dppDocumentId")]
        public string? DppDocumentId { get; set; }

        /// <summary>
        /// Safe display fields extracted from the invoice by Gemini.
        /// Confidential field values are stored as null; ciphertexts live in ExtractedField records.
        /// </summary>
        [BsonElement("invoiceFields")]
        public Dictionary<string, string?>? InvoiceFields { get; set; }

        // ── Quality Inspection Report Data ──────────────────────────────

        [BsonElement("qirPath")]
        public string? QirPath { get; set; }

        [BsonElement("qirClassification")]
        public string? QirClassification { get; set; }

        [BsonElement("qirEncryptionMetadata")]
        public string? QirEncryptionMetadata { get; set; }

        /// <summary>
        /// Safe display fields extracted from the QIR by Gemini.
        /// Confidential values (e.g. pricing-related) are null; ciphertexts in ExtractedField keyed by "qir_{id}".
        /// </summary>
        [BsonElement("qirFields")]
        public Dictionary<string, string?>? QirFields { get; set; }
    }

    public class TransactionMessage
    {
        public string SenderId { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
