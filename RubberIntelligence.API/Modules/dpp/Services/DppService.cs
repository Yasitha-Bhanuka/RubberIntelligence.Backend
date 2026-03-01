using MongoDB.Bson;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Modules.Dpp.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// Assembles a DigitalProductPassport from extracted field records.
    /// Only non-confidential fields are included in the DPP payload.
    /// A SHA-256 hash of the serialized object is stored for integrity.
    /// </summary>
    public class DppService
    {
        private readonly IDppRepository _repository;

        public DppService(IDppRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Generates and persists a DigitalProductPassport for the given lot.
        /// </summary>
        public async Task<DigitalProductPassport> GenerateDpp(string dppId)
        {
            // Step 1: Retrieve all ExtractedField records for this lot
            // Note: dppId == LotId — same MongoDB ObjectId, set at upload time
            var allFields = await _repository.GetExtractedFieldsByLotIdAsync(dppId);

            // Step 2: Separate confidential and non-confidential fields
            var publicFields = allFields
                .Where(f => !f.IsConfidential)
                .ToDictionary(f => f.FieldName, f => f.EncryptedValue, StringComparer.OrdinalIgnoreCase);

            bool hasConfidentialData = allFields.Any(f => f.IsConfidential);

            // Step 3: Populate DPP using ONLY non-confidential field values
            var dpp = new DigitalProductPassport
            {
                Id                     = ObjectId.GenerateNewId().ToString(),
                LotId                  = dppId,
                RubberGrade            = publicFields.GetValueOrDefault("rubberGrade", string.Empty),
                Quantity               = TryParseDouble(publicFields.GetValueOrDefault("quantity", "0")),
                DispatchDetails        = publicFields.GetValueOrDefault("dispatchPort", string.Empty),
                ConfidentialDataExists = hasConfidentialData,  // Step 4
                DppHash                = string.Empty,         // Filled after serialization
                CreatedAt              = DateTime.UtcNow
            };

            // Step 5: Serialize to JSON
            var json = JsonSerializer.Serialize(dpp, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Step 6 & 7: Compute SHA-256 hash and store it
            dpp.DppHash = ComputeSha256(json);

            // Step 8: Save to MongoDB
            await _repository.CreateDppAsync(dpp);

            return dpp;
        }

        // ── Private Helpers ──────────────────────────────────────────────

        private static double TryParseDouble(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            // Strip non-numeric chars e.g. "500 kg" → "500"
            var numeric = new string(value.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            return double.TryParse(numeric, out var result) ? result : 0;
        }

        private static string ComputeSha256(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
