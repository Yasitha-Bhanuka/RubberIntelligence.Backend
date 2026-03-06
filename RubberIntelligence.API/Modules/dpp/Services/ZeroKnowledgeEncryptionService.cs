using System.Security.Cryptography;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// Hybrid RSA + AES-256-CBC encryption for true zero-knowledge file protection.
    ///
    /// DESIGN INVARIANT
    /// ────────────────
    /// The backend NEVER possesses the RSA private key.  It encrypts with a random
    /// AES-256 session key, wraps that key with the exporter's RSA public key, then
    /// discards the plaintext AES key from memory.  Only the exporter's device —
    /// holding the RSA private key — can recover the AES key and decrypt the file.
    ///
    /// AES: 256-bit key, 128-bit CSPRNG IV, CBC + PKCS7.
    /// RSA: OAEP padding with SHA-256 — chosen over PKCS#1 v1.5 to prevent
    ///      Bleichenbacher-style padding-oracle attacks.
    /// </summary>
    public class ZeroKnowledgeEncryptionService
    {
        private readonly ILogger<ZeroKnowledgeEncryptionService> _logger;

        public ZeroKnowledgeEncryptionService(ILogger<ZeroKnowledgeEncryptionService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Encrypts <paramref name="fileBytes"/> with a random AES-256 key, then
        /// wraps the AES key with the exporter's RSA public key.
        ///
        /// The raw AES key is explicitly zeroed before the method returns.
        /// </summary>
        /// <param name="fileBytes">Raw file content to encrypt.</param>
        /// <param name="exporterPublicKeyXml">RSA public key in XML format (from the exporter's device).</param>
        /// <returns>A <see cref="HybridEncryptionResult"/> with three Base64 payloads.</returns>
        public HybridEncryptionResult EncryptDocumentHybrid(byte[] fileBytes, string exporterPublicKeyXml)
        {
            byte[] aesKey = Array.Empty<byte>();

            try
            {
                // ── 1. AES-256 session key + IV (CSPRNG) ─────────────────────────
                aesKey = RandomNumberGenerator.GetBytes(32);  // 256-bit key
                var iv = RandomNumberGenerator.GetBytes(16);  // 128-bit IV

                byte[] cipherBytes;
                using (var aes = Aes.Create())
                {
                    aes.Key     = aesKey;
                    aes.IV      = iv;
                    aes.Mode    = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using var encryptor = aes.CreateEncryptor();
                    cipherBytes = encryptor.TransformFinalBlock(fileBytes, 0, fileBytes.Length);
                }

                // ── 2. RSA-wrap the AES key with the exporter's public key ───────
                byte[] encryptedAesKey;
                using (var rsa = RSA.Create())
                {
                    rsa.FromXmlString(exporterPublicKeyXml);
                    encryptedAesKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
                }

                _logger.LogInformation(
                    "[ZeroKnowledge] Document encrypted — AES cipher {CipherLen} bytes, " +
                    "RSA-wrapped key {WrappedKeyLen} bytes.",
                    cipherBytes.Length, encryptedAesKey.Length);

                return new HybridEncryptionResult
                {
                    EncryptedVaultBase64  = Convert.ToBase64String(cipherBytes),
                    EncryptedAesKeyBase64 = Convert.ToBase64String(encryptedAesKey),
                    IvBase64              = Convert.ToBase64String(iv)
                };
            }
            finally
            {
                // ── 3. Zeroize the raw AES key — defense-in-depth ────────────────
                CryptographicOperations.ZeroMemory(aesKey);
            }
        }
    }

    /// <summary>
    /// Immutable result of hybrid RSA + AES encryption.
    /// All three fields are Base64-encoded and safe for JSON serialization.
    /// </summary>
    public class HybridEncryptionResult
    {
        /// <summary>AES-256-CBC ciphertext of the original file.</summary>
        public string EncryptedVaultBase64 { get; init; } = string.Empty;

        /// <summary>AES key encrypted with the exporter's RSA public key (OAEP SHA-256).</summary>
        public string EncryptedAesKeyBase64 { get; init; } = string.Empty;

        /// <summary>AES IV (128-bit) — not secret, but required for decryption.</summary>
        public string IvBase64 { get; init; } = string.Empty;
    }
}
