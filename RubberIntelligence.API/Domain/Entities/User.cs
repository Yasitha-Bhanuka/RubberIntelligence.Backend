using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver.GeoJsonObjectModel;
using RubberIntelligence.API.Domain.Enums;

namespace RubberIntelligence.API.Domain.Entities
{
    [BsonIgnoreExtraElements]
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)] // Store Guid as String for simplicity or standard BsonType.ObjectId if using native MongoDB IDs
        public Guid Id { get; set; }

        [BsonElement("fullName")]
        public required string FullName { get; set; }

        [BsonElement("email")]
        public required string Email { get; set; }

        [BsonElement("passwordHash")]
        public string? PasswordHash { get; set; }

        [BsonRepresentation(BsonType.String)]
        [BsonElement("role")]
        public UserRole Role { get; set; }

        // Plantation Information
        [BsonElement("plantationName")]
        public string? PlantationName { get; set; }

        [BsonElement("location")]
        public GeoJsonPoint<GeoJson2DGeographicCoordinates>? Location { get; set; }

        // Approval & Audit
        [BsonElement("isApproved")]
        public bool IsApproved { get; set; } = false;

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Security Keys for DPP Encryption
        [BsonElement("publicKey")]
        public string? PublicKey { get; set; }

        [BsonElement("privateKey")]
        public string? PrivateKey { get; set; }
    }
}
