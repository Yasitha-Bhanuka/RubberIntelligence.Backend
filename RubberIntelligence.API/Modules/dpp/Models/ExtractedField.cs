using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Dpp.Models
{
    public class ExtractedField
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string LotId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string DocumentId { get; set; } = string.Empty;

        public string FieldName { get; set; } = string.Empty;

        public string EncryptedValue { get; set; } = string.Empty;

        public string IV { get; set; } = string.Empty;

        public bool IsConfidential { get; set; }

        public double ConfidenceScore { get; set; }

        public bool ManualReviewRequired { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
