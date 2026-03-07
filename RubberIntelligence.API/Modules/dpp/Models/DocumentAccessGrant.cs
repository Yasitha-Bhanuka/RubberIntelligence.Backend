using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Dpp.Models
{
    /// <summary>
    /// Grants an exporter permission to decrypt a specific DppDocument.
    /// Created when a marketplace transaction is approved or exporter is given access.
    /// Stores the decrypted AES key so exporter can retrieve the encrypted file.
    /// </summary>
    public class DocumentAccessGrant
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Reference to the encrypted DppDocument
        /// </summary>
        public string DppDocumentId { get; set; } = string.Empty;

        /// <summary>
        /// Exporter user ID who has been granted access
        /// </summary>
        public string ExporterId { get; set; } = string.Empty;

        /// <summary>
        /// The AES-256 decryption key for this document (Base64 encoded).
        /// This is the plaintext key — exporter uses this to decrypt the file.
        /// Access to this collection should be restricted.
        /// </summary>
        public string DecryptionKey { get; set; } = string.Empty;

        /// <summary>
        /// When access was granted (transaction approval, manual grant, etc.)
        /// </summary>
        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Who granted access (buyer ID, admin ID, system)
        /// </summary>
        public string GrantedBy { get; set; } = string.Empty;

        /// <summary>
        /// Optional: transaction ID if this grant was created from marketplace transaction
        /// </summary>
        public string? TransactionId { get; set; }
    }
}
