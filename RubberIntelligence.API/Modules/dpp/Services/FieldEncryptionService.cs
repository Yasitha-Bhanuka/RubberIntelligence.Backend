using System.Security.Cryptography;
using System.Text;
using RubberIntelligence.API.Infrastructure.Security;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// AES-256-CBC field-level encryption service.
    ///
    /// KEY MANAGEMENT
    /// ──────────────
    /// The AES key is never hardcoded here.  It is resolved at startup by
    /// <see cref="EncryptionKeyProvider"/> using the chain:
    ///   env-var DPP_FIELD_ENCRYPTION_KEY → appsettings config → dev-only fallback.
    /// In any non-Development environment the provider throws at startup if the
    /// key is missing, preventing the API from starting without a real secret.
    ///
    /// IV INTEGRITY
    /// ────────────
    /// Every call to <see cref="Encrypt"/> generates a fresh 16-byte IV via
    /// <see cref="RandomNumberGenerator.GetBytes"/> (CSPRNG — not Math.Random).
    /// The IV is stored in a dedicated MongoDB field so decryption never has to
    /// guess or reconstruct it.  Reusing the same IV with the same key would allow
    /// pattern-matching across records — the per-call generation prevents this.
    ///
    /// KEY-VERSION TAGGING
    /// ───────────────────
    /// EncryptedValue is prefixed with "v{version}:" so that a future key rotation
    /// can identify which key to use for decryption during the migration window.
    /// </summary>
    public class FieldEncryptionService
    {
        private readonly byte[] _key;
        private readonly int    _keyVersion;
        private const    int    IvSizeBytes = 16; // AES block size

        public FieldEncryptionService(EncryptionKeyProvider keyProvider)
        {
            _key        = keyProvider.GetFieldAesKey();
            _keyVersion = keyProvider.CurrentKeyVersion;
        }

        /// <summary>
        /// Encrypts a plaintext string using AES-256-CBC with a CSPRNG-generated IV.
        /// Returns EncryptedValue ("v{ver}:" + Base64) and IV (Base64).
        /// </summary>
        public EncryptionResult Encrypt(string plainText)
        {
            // ── Generate a cryptographically random IV for this operation only ──
            // RandomNumberGenerator.GetBytes is CSPRNG-backed; never reuse across calls.
            var iv = RandomNumberGenerator.GetBytes(IvSizeBytes);

            using var aes = Aes.Create();
            aes.Key     = _key;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var plainBytes     = Encoding.UTF8.GetBytes(plainText);
            using var encryptor = aes.CreateEncryptor();
            var cipherBytes    = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return new EncryptionResult
            {
                // Version prefix lets key-rotation logic choose the right key during decrypt
                EncryptedValue = $"v{_keyVersion}:{Convert.ToBase64String(cipherBytes)}",
                IV             = Convert.ToBase64String(iv)
            };
        }

        /// <summary>
        /// Decrypts an AES-256-CBC value produced by <see cref="Encrypt"/>.
        /// Handles both versioned ("v1:...") and legacy (plain Base64) formats.
        /// </summary>
        public string Decrypt(string encryptedValue, string iv)
        {
            // Strip key-version prefix if present
            var cipherBase64 = encryptedValue.Contains(':')
                ? encryptedValue[(encryptedValue.IndexOf(':') + 1)..]
                : encryptedValue;

            using var aes = Aes.Create();
            aes.Key     = _key;
            aes.IV      = Convert.FromBase64String(iv);
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var cipherBytes    = Convert.FromBase64String(cipherBase64);
            using var decryptor = aes.CreateDecryptor();
            var plainBytes     = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

            return Encoding.UTF8.GetString(plainBytes);
        }
    }

    /// <summary>
    /// Holds the result of a field encryption operation.
    /// </summary>
    public class EncryptionResult
    {
        /// <summary>Base64 ciphertext prefixed with key version (e.g., "v1:&lt;base64&gt;").</summary>
        public string EncryptedValue { get; set; } = string.Empty;

        /// <summary>Base64-encoded AES IV — unique per encryption call.</summary>
        public string IV { get; set; } = string.Empty;
    }
}
