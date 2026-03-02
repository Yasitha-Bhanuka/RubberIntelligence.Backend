using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Dpp.Models
{
    /// <summary>
    /// Lot-Linked Message for secure communication between Buyer and Exporter.
    /// If IsConfidential = true, EncryptedContent holds AES-256-CBC ciphertext and IV is populated.
    /// If IsConfidential = false, EncryptedContent holds plaintext and IV is empty.
    /// Decryption happens ONLY in MessageService — never in the controller.
    /// </summary>
    public class Message
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonRepresentation(BsonType.ObjectId)]
        public string LotId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.String)]
        public string SenderId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.String)]
        public string ReceiverId { get; set; } = string.Empty;

        // AES-256-CBC ciphertext (Base64) if IsConfidential; plaintext otherwise
        public string EncryptedContent { get; set; } = string.Empty;

        // Base64-encoded IV; empty string when IsConfidential = false
        public string IV { get; set; } = string.Empty;

        public bool IsConfidential { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
