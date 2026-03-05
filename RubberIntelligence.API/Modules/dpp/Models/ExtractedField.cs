using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Dpp.Models
{
    public class ExtractedField
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        // Plain string — may contain non-ObjectId values such as "qir_{transactionId}"
        public string LotId { get; set; } = string.Empty;

        // Plain string — stores the parent document / transaction ID as-is
        public string DocumentId { get; set; } = string.Empty;

        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// AES-256-CBC ciphertext prefixed with key-version tag (e.g., "v1:&lt;base64&gt;").
        /// For non-confidential fields this holds the plain-text value.
        /// </summary>
        public string EncryptedValue { get; set; } = string.Empty;

        /// <summary>Base64-encoded AES IV — unique per encryption call.  Empty for non-confidential fields.</summary>
        public string IV { get; set; } = string.Empty;

        public bool IsConfidential { get; set; }

        public double ConfidenceScore { get; set; }

        public bool ManualReviewRequired { get; set; }

        /// <summary>
        /// HMAC-SHA256 blind index over (fieldName | normalised plaintext).
        /// Enables equality searches on confidential fields without decrypting.
        /// Only populated for IsConfidential == true.
        /// </summary>
        public string? BlindIndex { get; set; }

        /// <summary>Key version at encryption time. Used for key-rotation decryption routing.</summary>
        public int KeyVersion { get; set; } = 1;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
