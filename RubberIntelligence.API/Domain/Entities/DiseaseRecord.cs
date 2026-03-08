using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver.GeoJsonObjectModel;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Domain.Entities
{
    public class DiseaseRecord
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public Guid Id { get; set; }

        [BsonRepresentation(BsonType.String)]
        public Guid UserId { get; set; }

        [BsonElement("diseaseType")]
        [BsonRepresentation(BsonType.String)]
        public DiseaseType DiseaseType { get; set; }

        [BsonElement("predictedLabel")]
        public required string PredictedLabel { get; set; }

        [BsonElement("confidence")]
        public double Confidence { get; set; }

        [BsonElement("imagePath")]
        public string? ImagePath { get; set; } // Path to stored image if we save it

        [BsonElement("timestamp")]
        public DateTime Timestamp { get; set; }

        [BsonElement("location")]
        public GeoJsonPoint<GeoJson2DGeographicCoordinates>? Location { get; set; }

        [BsonElement("severity")]
        public string Severity { get; set; } = "Low";

        [BsonElement("isRejected")]
        public bool IsRejected { get; set; } = false;
    }
}

