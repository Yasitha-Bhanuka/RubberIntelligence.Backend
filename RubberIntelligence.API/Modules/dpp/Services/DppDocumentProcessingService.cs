using MongoDB.Bson;
using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// Orchestrates per-field confidentiality classification and AES-256 encryption.
    /// Keeps the controller free of encryption logic — fulfilling clean-architecture Req #7.
    /// </summary>
    public class DppDocumentProcessingService
    {
        private readonly FieldConfidentialityService _confidentialityService;
        private readonly FieldEncryptionService _encryptionService;

        public DppDocumentProcessingService(
            FieldConfidentialityService confidentialityService,
            FieldEncryptionService encryptionService)
        {
            _confidentialityService = confidentialityService;
            _encryptionService      = encryptionService;
        }

        /// <summary>
        /// For each extracted field:
        ///   - Classifies it as confidential or non-confidential.
        ///   - If confidential: encrypts with AES-256-CBC + random IV, stores ciphertext + IV.
        ///   - If non-confidential: stores plain value, IV = "".
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

                if (classification.IsConfidential && !string.IsNullOrWhiteSpace(plainValue))
                {
                    // Encrypt: AES-256-CBC, random IV per field, NEVER store plaintext
                    var encrypted = _encryptionService.Encrypt(plainValue);
                    storedValue   = encrypted.EncryptedValue;
                    iv            = encrypted.IV;
                }
                else
                {
                    // Non-confidential: plain value stored, IV left empty
                    storedValue = plainValue;
                    iv          = string.Empty;
                }

                fieldRecords.Add(new ExtractedField
                {
                    Id                   = ObjectId.GenerateNewId().ToString(),
                    DocumentId           = documentId,
                    LotId                = documentId,  // LotId == dppDoc.Id by design (see DppService)
                    FieldName            = fieldName,
                    EncryptedValue       = storedValue,
                    IV                   = iv,
                    IsConfidential       = classification.IsConfidential,
                    ConfidenceScore      = classification.ConfidenceScore,
                    ManualReviewRequired = classification.ManualReviewRequired,
                    CreatedAt            = DateTime.UtcNow
                });
            }

            return fieldRecords;
        }
    }
}
