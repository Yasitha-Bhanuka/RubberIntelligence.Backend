using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using RubberIntelligence.API.Domain.Enums;

namespace RubberIntelligence.API.Domain.Entities
{
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
    }
}
