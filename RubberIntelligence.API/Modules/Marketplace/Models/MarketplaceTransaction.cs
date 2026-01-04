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
        public string Status { get; set; } = "Completed"; // Completed (Direct Buy)

        [BsonElement("offerPrice")]
        public decimal OfferPrice { get; set; }

        [BsonElement("lastUpdatedAt")]
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TransactionMessage
    {
        public string SenderId { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
