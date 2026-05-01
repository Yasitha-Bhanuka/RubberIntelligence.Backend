using MongoDB.Bson;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Modules.Dpp.DTOs;
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
        // ── Private Helpers ──────────────────────────────────────────────

        private static DateTime GetTruncatedUtcNow()
        {
            // MongoDB stores DateTimes with millisecond precision.
            // Truncate ticks to ensure the JSON serialized during generation
            // EXACTLY matches the JSON serialized after retrieval from the database.
            var now = DateTime.UtcNow;
            return new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond), now.Kind);
        }

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
                LifecycleState         = "GENERATED",
                DppHash                = string.Empty,         // Filled after serialization
                CreatedAt              = GetTruncatedUtcNow()  // Millisecond precision to match MongoDB
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

        /// <summary>
        /// Verifies a stored DPP's SHA-256 hash by re-serializing the passport
        /// with the same camelCase format used at generation time (DppHash cleared to "").
        /// </summary>
        public async Task<DppVerificationResponseDto> VerifyDppHash(string lotId)
        {
            var dpp = await _repository.GetDppByLotIdAsync(lotId)
                ?? throw new KeyNotFoundException($"No DPP found for lot {lotId}.");

            var storedHash = dpp.DppHash;

            // Re-create the exact serialization state used when the hash was first computed.
            // DppHash must be empty, and state must reflect the generation moment to reproduce original JSON.
            var snapshot = new DigitalProductPassport
            {
                Id                     = dpp.Id,
                LotId                  = dpp.LotId,
                RubberGrade            = dpp.RubberGrade,
                Quantity               = dpp.Quantity,
                DispatchDetails        = dpp.DispatchDetails,
                ConfidentialDataExists = dpp.ConfidentialDataExists,
                LifecycleState         = "GENERATED",    // Force generation-time state for hashing
                DppHash                = string.Empty,   // cleared — matches generation-time state
                CreatedAt              = dpp.CreatedAt   // Truncated milliseconds retrieved from DB
            };

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var recalculated = ComputeSha256(json);

            return new DppVerificationResponseDto
            {
                IsValid           = string.Equals(storedHash, recalculated, StringComparison.OrdinalIgnoreCase),
                RecalculatedHash  = recalculated,
                StoredHash        = storedHash
            };
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
