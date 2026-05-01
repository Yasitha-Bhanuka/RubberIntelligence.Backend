using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Dpp.Models
{
    public class DppDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFilePath { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;

        public string Classification { get; set; } = string.Empty;
        public double ConfidenceScore { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public string UploadedBy { get; set; } = string.Empty;

        public string? ExtractedTextSummary { get; set; }
        public List<string> DetectedKeywords { get; set; } = new();

        /// <summary>
        /// True when the majority of extracted fields were confidential and the
        /// original file was replaced with an AES-256-CBC encrypted copy.
        /// </summary>
        public bool IsDocumentEncrypted { get; set; }

        /// <summary>
        /// Absolute server path to the AES-256-CBC encrypted file.
        /// Only populated when <see cref="IsDocumentEncrypted"/> is true.
        /// </summary>
        public string? EncryptedFilePath { get; set; }

        /// <summary>
        /// The AES-256 key used to encrypt the file, itself encrypted with RSA-2048.
        /// Base64-encoded ciphertext. Only populated when IsDocumentEncrypted == true.
        /// Decryption requires the RSA private key from EncryptionKeyProvider.
        /// </summary>
        public string? EncryptedAesKey { get; set; }

        /// <summary>
        /// RSA encryption algorithm + key version used to encrypt the AES key.
        /// e.g., "RSA-2048-v1". Used for key rotation support.
        /// </summary>
        public string? KeyEncryptionAlgorithm { get; set; }
    }
}
