using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Data.Repositories
{
    public interface IDocumentAccessGrantRepository
    {
        /// <summary>
        /// Creates a new access grant record for an exporter to decrypt a specific document.
        /// </summary>
        Task CreateGrantAsync(DocumentAccessGrant grant);

        /// <summary>
        /// Retrieves a specific grant by document ID and exporter ID.
        /// </summary>
        Task<DocumentAccessGrant?> GetGrantAsync(string dppDocumentId, string exporterId);

        /// <summary>
        /// Retrieves all grants for a specific exporter (to list accessible documents).
        /// </summary>
        Task<List<DocumentAccessGrant>> GetGrantsByExporterIdAsync(string exporterId);

        /// <summary>
        /// Retrieves all grants for a specific document (to see who has access).
        /// </summary>
        Task<List<DocumentAccessGrant>> GetGrantsByDocumentIdAsync(string dppDocumentId);

        /// <summary>
        /// Revokes access by deleting the grant record.
        /// </summary>
        Task RevokeGrantAsync(string grantId);

        /// <summary>
        /// Checks if an exporter has an active grant for a document.
        /// </summary>
        Task<bool> HasAccessAsync(string dppDocumentId, string exporterId);
    }
}
