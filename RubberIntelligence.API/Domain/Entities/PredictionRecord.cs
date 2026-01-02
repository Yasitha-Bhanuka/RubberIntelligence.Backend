using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Domain.Entities
{
    public class PredictionRecord
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public Guid Id { get; set; }

        // We can optionally link to a User if authentication is added later
        // [BsonRepresentation(BsonType.String)]
        // public Guid? UserId { get; set; }

        [BsonElement("timestamp")]
        public DateTime Timestamp { get; set; }

        // Inputs
        [BsonElement("rubberSheetGrade")]
        public string? RubberSheetGrade { get; set; }

        [BsonElement("quantityKg")]
        public float QuantityKg { get; set; }

        [BsonElement("moistureLevel")]
        public string? MoistureLevel { get; set; }

        [BsonElement("cleanliness")]
        public string? Cleanliness { get; set; }

        [BsonElement("visualQualityScore")]
        public float VisualQualityScore { get; set; }

        [BsonElement("district")]
        public string? District { get; set; }

        [BsonElement("marketAvailability")]
        public string? MarketAvailability { get; set; }

        // Output
        [BsonElement("predictedPriceLkr")]
        public float PredictedPriceLkr { get; set; }
    }
}
