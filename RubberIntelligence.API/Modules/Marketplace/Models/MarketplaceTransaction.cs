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
        public string Status { get; set; } = "PendingInvoice"; // Default start state for Buy Now

        [BsonElement("offerPrice")]
        public decimal OfferPrice { get; set; }

        [BsonElement("lastUpdatedAt")]
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

        // DPP & Invoice Data
        [BsonElement("dppInvoicePath")]
        public string? DppInvoicePath { get; set; }

        [BsonElement("dppClassification")]
        public string? DppClassification { get; set; }

        [BsonElement("encryptionMetadata")]
        public string? EncryptionMetadata { get; set; } // JSON: { "IV": "...", "EncryptedKey": "..." }

        [BsonElement("dppDocumentId")]
        public string? DppDocumentId { get; set; }
    }

    public class TransactionMessage
    {
        public string SenderId { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
