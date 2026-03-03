using RubberIntelligence.API.Modules.Dpp.DTOs;
using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// Handles the controlled-access workflow:
    ///   Exporter submits request → Buyer approves → Exporter receives decrypted fields.
    /// Decryption ONLY happens here — never in the controller.
    /// Plaintext values are never stored in DB.
    /// </summary>
    public class ConfidentialAccessService
    {
        private readonly FieldEncryptionService _encryptionService;
        private readonly ILogger<ConfidentialAccessService> _logger;

        public ConfidentialAccessService(
            FieldEncryptionService encryptionService,
            ILogger<ConfidentialAccessService> logger)
        {
            _encryptionService = encryptionService;
            _logger            = logger;
        }

        /// <summary>
        /// Decrypts all confidential ExtractedField records for a given lot.
        ///
        /// DEFENSE-IN-DEPTH GATE:
        ///   Even though the controller already verified the AccessRequest at the DB level,
        ///   this method re-verifies the status and identity here in the service layer.
        ///   This prevents a logic error or future refactor from bypassing the access check.
        ///
        /// Throws <see cref="UnauthorizedAccessException"/> if:
        ///   - The AccessRequest status is not APPROVED.
        ///   - The callerExporterId does not match the request's ExporterId.
        /// </summary>
        /// <param name="approvedRequest">The AccessRequest that grants this exporter access.</param>
        /// <param name="callerExporterId">The authenticated exporter's ID (from JWT).</param>
        /// <param name="fields">The confidential ExtractedField records to decrypt.</param>
        public List<ConfidentialFieldDto> DecryptFields(
            AccessRequest approvedRequest,
            string callerExporterId,
            IEnumerable<ExtractedField> fields)
        {
            // ── Service-layer gate (defense-in-depth) ────────────────────────────────
            // This check is intentionally redundant with the controller gate.
            // Two independent checks are harder to accidentally bypass.
            if (approvedRequest.Status != AccessRequestStatus.Approved)
                throw new UnauthorizedAccessException(
                    $"Access denied — request '{approvedRequest.Id}' has status " +
                    $"'{approvedRequest.Status}', not '{AccessRequestStatus.Approved}'. " +
                    "Decryption is only permitted for APPROVED requests.");

            if (!string.Equals(approvedRequest.ExporterId, callerExporterId, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    $"Access denied — the authenticated exporter '{callerExporterId}' does not " +
                    $"match the request's exporter '{approvedRequest.ExporterId}'.");
            // ─────────────────────────────────────────────────────────────────────────

            var results = new List<ConfidentialFieldDto>();

            foreach (var field in fields.Where(f => f.IsConfidential))
            {
                try
                {
                    var plaintext = _encryptionService.Decrypt(field.EncryptedValue, field.IV);
                    results.Add(new ConfidentialFieldDto
                    {
                        FieldName      = field.FieldName,
                        DecryptedValue = plaintext
                    });
                }
                catch (Exception ex)
                {
                    // Log the field-level decryption failure but do not surface the exception.
                    // Returning a typed error token per field keeps the response structure intact.
                    _logger.LogError(ex,
                        "[ConfidentialAccess] Decryption failed for field '{FieldName}' in lot '{LotId}'",
                        field.FieldName, field.LotId);

                    results.Add(new ConfidentialFieldDto
                    {
                        FieldName      = field.FieldName,
                        DecryptedValue = "[decryption error — contact system administrator]"
                    });
                }
            }

            _logger.LogInformation(
                "[ConfidentialAccess] Exporter '{ExporterId}' decrypted {Count} field(s) " +
                "for lot '{LotId}' via approved request '{RequestId}'.",
                callerExporterId, results.Count, approvedRequest.LotId, approvedRequest.Id);

            return results;
        }
    }
}
