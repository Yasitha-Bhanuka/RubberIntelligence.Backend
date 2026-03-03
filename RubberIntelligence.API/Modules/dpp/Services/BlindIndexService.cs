using System.Security.Cryptography;
using System.Text;
using RubberIntelligence.API.Infrastructure.Security;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// Produces a deterministic HMAC-SHA256 "blind index" for a confidential field value.
    ///
    /// WHY THIS EXISTS
    /// ───────────────
    /// AES-CBC produces a different ciphertext every time (because the IV is randomised),
    /// so you cannot run a database equality search on an encrypted column.  A blind index
    /// solves this: HMAC(hmacKey, normalise(value)) is deterministic — two identical values
    /// always produce the same hash — yet it reveals nothing about the plaintext to anyone
    /// who does not possess the HMAC key.
    ///
    /// HOW TO SEARCH
    /// ─────────────
    ///   var idx = blindIndexService.Compute("supplier", "PT Rubber Jaya");
    ///   var docs = await _dppRepository.FindByBlindIndexAsync("supplier", idx);
    ///
    /// SECURITY NOTES
    /// ──────────────
    /// - The HMAC key MUST be different from the AES encryption key.
    /// - Normalisation (lowercase + trim) prevents trivial bypass via case variation.
    /// - For low-cardinality fields (e.g., a boolean flag) a blind index leaks the
    ///   probable value distribution — restrict its use to high-entropy fields only.
    /// </summary>
    public sealed class BlindIndexService
    {
        private readonly byte[] _hmacKey;

        public BlindIndexService(EncryptionKeyProvider keyProvider)
        {
            _hmacKey = keyProvider.GetHmacKey();
        }

        /// <summary>
        /// Computes HMAC-SHA256( hmacKey, "<fieldName>|<normalised(value)>" ).
        /// Scoping the hash to fieldName prevents cross-field hash collisions.
        /// Returns a URL-safe Base64 string (43 chars).
        /// </summary>
        public string Compute(string fieldName, string plainValue)
        {
            // Normalise to reduce trivial bypass via whitespace / case differences
            var normalised = $"{fieldName.ToLowerInvariant()}|{plainValue.Trim().ToLowerInvariant()}";
            var inputBytes = Encoding.UTF8.GetBytes(normalised);

            using var hmac = new HMACSHA256(_hmacKey);
            var hash = hmac.ComputeHash(inputBytes);

            // URL-safe Base64 — no '+' or '/' — safe to store in MongoDB and use in queries
            return Convert.ToBase64String(hash)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        /// <summary>
        /// Convenience overload that accepts an already-normalised search term
        /// (e.g., from a user search box).  Delegates to <see cref="Compute"/>.
        /// </summary>
        public string ComputeForSearch(string fieldName, string searchTerm)
            => Compute(fieldName, searchTerm);
    }
}
