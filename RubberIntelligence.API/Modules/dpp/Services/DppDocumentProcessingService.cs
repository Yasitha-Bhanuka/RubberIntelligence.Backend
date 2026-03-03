using MongoDB.Bson;
using RubberIntelligence.API.Modules.Dpp.Models;
using RubberIntelligence.API.Infrastructure.Security;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// Orchestrates per-field confidentiality classification, AES-256-CBC encryption,
    /// and HMAC-SHA256 blind-index generation.
    /// Keeps the controller free of encryption logic — fulfilling clean-architecture Req #7.
    /// </summary>
    public class DppDocumentProcessingService
    {
        private readonly FieldConfidentialityService _confidentialityService;
        private readonly FieldEncryptionService      _encryptionService;
        private readonly BlindIndexService           _blindIndexService;
        private readonly EncryptionKeyProvider       _keyProvider;

        public DppDocumentProcessingService(
            FieldConfidentialityService confidentialityService,
            FieldEncryptionService      encryptionService,
            BlindIndexService           blindIndexService,
            EncryptionKeyProvider       keyProvider)
        {
            _confidentialityService = confidentialityService;
            _encryptionService      = encryptionService;
            _blindIndexService      = blindIndexService;
            _keyProvider            = keyProvider;
        }

        /// <summary>
        /// For each extracted field:
        ///   - Classifies it as confidential or non-confidential.
        ///   - If confidential:
        ///       • Encrypts with AES-256-CBC + CSPRNG IV (key-version tagged).
        ///       • Generates HMAC-SHA256 blind index for future searchability.
        ///   - If non-confidential: stores plain value, IV = "", BlindIndex = null.
        ///
        /// Plaintext confidential values are NEVER returned or persisted by this method.
        /// </summary>
        public List<ExtractedField> ProcessFields(
            Dictionary<string, string> extractedFields,
            string documentId)
        {
            var fieldRecords = new List<ExtractedField>();

            foreach (var (fieldName, plainValue) in extractedFields)
            {
                var classification = _confidentialityService.Classify(fieldName, plainValue);

                string storedValue;
                string iv;
                string? blindIndex = null;

                if (classification.IsConfidential && !string.IsNullOrWhiteSpace(plainValue))
                {
                    // AES-256-CBC — CSPRNG IV per field, key-version prefix on ciphertext
                    var encrypted = _encryptionService.Encrypt(plainValue);
                    storedValue   = encrypted.EncryptedValue;
                    iv            = encrypted.IV;

                    // HMAC blind index — deterministic hash for future equality searches
                    blindIndex = _blindIndexService.Compute(fieldName, plainValue);
                }
                else
                {
                    // Non-confidential: plain value stored, no IV, no blind index needed
                    storedValue = plainValue;
                    iv          = string.Empty;
                }

                fieldRecords.Add(new ExtractedField
                {
                    Id                   = ObjectId.GenerateNewId().ToString(),
                    DocumentId           = documentId,
                    LotId                = documentId,
                    FieldName            = fieldName,
                    EncryptedValue       = storedValue,
                    IV                   = iv,
                    IsConfidential       = classification.IsConfidential,
                    ConfidenceScore      = classification.ConfidenceScore,
                    ManualReviewRequired = classification.ManualReviewRequired,
                    BlindIndex           = blindIndex,
                    KeyVersion           = _keyProvider.CurrentKeyVersion,
                    CreatedAt            = DateTime.UtcNow
                });
            }

            return fieldRecords;
        }
    }
}
