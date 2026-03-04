using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Dpp.Models
{
    public class AccessRequest
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        /// <summary>LotId == DppDocument.Id (the upload's MongoDB ObjectId)</summary>
        [BsonRepresentation(BsonType.ObjectId)]
        public string LotId { get; set; } = string.Empty;

        /// <summary>The exporter who is requesting access to confidential fields.</summary>
        public string ExporterId { get; set; } = string.Empty;

        /// <summary>The buyer who owns this DPP — auto-populated from DppDocument.UploadedBy</summary>
        public string BuyerId { get; set; } = string.Empty;

        /// <summary>PENDING | APPROVED | REJECTED</summary>
        public string Status { get; set; } = AccessRequestStatus.Pending;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ApprovedAt { get; set; }
    }

    public static class AccessRequestStatus
    {
        public const string Pending  = "PENDING";
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";
    }
}
