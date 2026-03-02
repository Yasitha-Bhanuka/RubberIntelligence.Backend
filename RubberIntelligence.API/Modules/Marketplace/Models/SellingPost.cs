using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Marketplace.Models
{
    public class SellingPost
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.String)]
        public string BuyerId { get; set; } = string.Empty;

        public string BuyerName { get; set; } = string.Empty; // Cached for display

        // Lot Details
        public string Grade { get; set; } = string.Empty; // RSS1, RSS2, etc.
        public double QuantityKg { get; set; }
        public double PricePerKg { get; set; }
        public string Currency { get; set; } = "LKR";
        public string Location { get; set; } = string.Empty;
        
        // Link to DPP Proof
        public string? DppDocumentId { get; set; }

        [BsonElement("status")]
        // Lifecycle statuses: AVAILABLE, REQUESTED, APPROVED, COMPLETED, REJECTED, REINSPECTION
        // Legacy statuses (backward-compat): Active, Sold, Archived
        public string Status { get; set; } = "AVAILABLE";

        [BsonElement("soldToExporterId")]
        public string? SoldToExporterId { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
