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

        public ConfidentialAccessService(FieldEncryptionService encryptionService)
        {
            _encryptionService = encryptionService;
        }

        /// <summary>
        /// Decrypts all confidential ExtractedField records for a given lot.
        /// Caller MUST verify that an APPROVED AccessRequest exists before calling.
        /// </summary>
        public List<ConfidentialFieldDto> DecryptFields(IEnumerable<ExtractedField> fields)
        {
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
                catch
                {
                    // If decryption fails for a field, skip it rather than crash
                    results.Add(new ConfidentialFieldDto
                    {
                        FieldName      = field.FieldName,
                        DecryptedValue = "[decryption error]"
                    });
                }
            }

            return results;
        }
    }
}
