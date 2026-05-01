using System.Security.Cryptography;
using System.Text;

namespace RubberIntelligence.API.Modules.Marketplace.Services
{
    /// <summary>
    /// Conditional "Bank-Statement" Zero-Knowledge Encryption.
    ///
    /// ENCRYPTION MODEL
    /// ────────────────
    /// PBKDF2-SHA256 (100 000 iterations) derives a 256-bit AES key and a 128-bit IV
    /// using:
    ///   Password = SecretRequestId  (known ONLY to the purchasing exporter)
    ///   Salt     = transactionId    (lot-specific, public)
    ///
    /// The backend encrypts the raw file bytes with AES-256-CBC and stores only the
    /// ciphertext (Base64).  The SecretRequestId is permanently nullified in the database
    /// immediately after encryption — it is never stored alongside the ciphertext.
    ///
    /// KEY DELIVERY
    /// ────────────
    /// The SecretRequestId is delivered to the Exporter via a one-time
    /// GET /transactions/{id}/my-secret endpoint (ReBAC-protected).
    /// The Buyer who uploads the document never sees the key.
    ///
    /// ZERO-KNOWLEDGE PROPERTY
    /// ───────────────────────
    /// Because the key material (SecretRequestId) is nullified after encryption,
    /// even a full database breach cannot reveal the content of CONFIDENTIAL documents.
    /// Decryption happens entirely on the exporter's device (client-side).
    ///
    /// SECURITY PARAMETERS
    /// ───────────────────
    /// • Algorithm : AES-256-CBC with PKCS#7 padding
    /// • KDF       : PBKDF2 / HMAC-SHA256 / 100 000 iterations
    /// • Key length: 32 bytes (256 bit)
    /// • IV length : 16 bytes (128 bit, AES block size)
    /// • Salt      : UTF-8 bytes of the transaction ID (lot-specific, prevents cross-lot attack)
    /// </summary>
    public class ZeroKnowledgeEncryptionService
    {
        private const int Iterations  = 100_000;
        private const int KeyBytes    = 32; // AES-256
        private const int IvBytes     = 16; // AES block size

        /// <summary>
        /// Encrypts <paramref name="fileBytes"/> using PBKDF2-AES-256-CBC.
        /// Returns (ciphertextBase64, ivBase64).  The IV is derived deterministically
        /// from the same KDF so the client only needs the SecretRequestId to decrypt.
        /// </summary>
        public (string CiphertextBase64, string IvBase64) EncryptDocumentBankStatement(
            byte[] fileBytes, string secretRequestId, string transactionId)
        {
            var saltBytes = Encoding.UTF8.GetBytes(transactionId);

            // Derive key (first 32 bytes) and IV (next 16 bytes) in one PBKDF2 call.
            using var pbkdf2 = new Rfc2898DeriveBytes(
                secretRequestId, saltBytes, Iterations, HashAlgorithmName.SHA256);

            var key = pbkdf2.GetBytes(KeyBytes);
            var iv  = pbkdf2.GetBytes(IvBytes);

            using var aes = Aes.Create();
            aes.Key     = key;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor   = aes.CreateEncryptor();
            var       cipherBytes = encryptor.TransformFinalBlock(fileBytes, 0, fileBytes.Length);

            return (Convert.ToBase64String(cipherBytes), Convert.ToBase64String(iv));
        }

        /// <summary>
        /// Server-side decryption helper (used if the server ever needs to decrypt,
        /// e.g. for admin review).  Not called in the normal client-side-decrypt flow.
        /// </summary>
        public byte[] Decrypt(
            string ciphertextBase64, string secretRequestId, string transactionId)
        {
            var saltBytes = Encoding.UTF8.GetBytes(transactionId);

            using var pbkdf2 = new Rfc2898DeriveBytes(
                secretRequestId, saltBytes, Iterations, HashAlgorithmName.SHA256);

            var key = pbkdf2.GetBytes(KeyBytes);
            var iv  = pbkdf2.GetBytes(IvBytes);

            using var aes = Aes.Create();
            aes.Key     = key;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var cipherBytes = Convert.FromBase64String(ciphertextBase64);
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        }
    }
}
