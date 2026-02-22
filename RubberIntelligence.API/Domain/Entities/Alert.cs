using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Domain.Entities
{
    public class Alert
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public Guid Id { get; set; }

        [BsonRepresentation(BsonType.String)]
        [BsonElement("farmerId")]
        public Guid FarmerId { get; set; }

        [BsonRepresentation(BsonType.String)]
        [BsonElement("detectionId")]
        public Guid DetectionId { get; set; }

        [BsonElement("diseaseName")]
        public required string DiseaseName { get; set; }

        [BsonElement("distanceKm")]
        public double DistanceKm { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }

        [BsonElement("isRead")]
        public bool IsRead { get; set; }
    }
}
