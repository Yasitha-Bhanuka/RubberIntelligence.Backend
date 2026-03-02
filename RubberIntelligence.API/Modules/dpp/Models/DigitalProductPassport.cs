using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Dpp.Models
{
    public class DigitalProductPassport
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonRepresentation(BsonType.ObjectId)]
        public string LotId { get; set; } = string.Empty;

        public string RubberGrade { get; set; } = string.Empty;

        public double Quantity { get; set; }

        public string DispatchDetails { get; set; } = string.Empty;

        public bool ConfidentialDataExists { get; set; }

        // Lifecycle: GENERATED, VERIFIED, REINSPECTION_REQUESTED, ACCEPTED, REJECTED
        public string LifecycleState { get; set; } = "GENERATED";

        public string DppHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
