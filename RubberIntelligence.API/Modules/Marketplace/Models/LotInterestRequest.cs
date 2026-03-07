using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Marketplace.Models
{
    /// <summary>
    /// Tracks an exporter's request to purchase a specific rubber lot.
    /// Created when exporter clicks "Request Purchase" on a lot.
    /// Buyer reviews and accepts one exporter, which creates a MarketplaceTransaction.
    /// </summary>
    public class LotInterestRequest
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("postId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string PostId { get; set; } = string.Empty;

        [BsonElement("exporterId")]
        public string ExporterId { get; set; } = string.Empty;

        [BsonElement("exporterName")]
        public string ExporterName { get; set; } = string.Empty;

        [BsonElement("country")]
        public string? Country { get; set; }

        [BsonElement("organizationType")]
        public string? OrganizationType { get; set; }

        [BsonElement("isVerified")]
        public bool IsVerified { get; set; }

        [BsonElement("platformTenureMonths")]
        public int PlatformTenureMonths { get; set; }

        [BsonElement("successfulCollaborations")]
        public int SuccessfulCollaborations { get; set; }

        [BsonElement("trustScore")]
        public double TrustScore { get; set; }

        /// <summary>PENDING | ACCEPTED | REJECTED</summary>
        [BsonElement("status")]
        public string Status { get; set; } = "PENDING";

        [BsonElement("requestedAt")]
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }
}
