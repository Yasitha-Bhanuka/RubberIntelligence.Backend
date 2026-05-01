using System.Security.Cryptography;
using System.Text;
using RubberIntelligence.API.Infrastructure.Security;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// AES-256-CBC file-level encryption service with hybrid asymmetric support.
    ///
    /// KEY MANAGEMENT (Hybrid Encryption)
    /// ──────────────────────────────────
    /// - Each file is encrypted with a unique AES-256 key (fast symmetric encryption)
    /// - The AES key is then encrypted with RSA-2048 (asymmetric wrapping)
    /// - File decryption requires: decrypt RSA-wrapped key → decrypt file with AES key
    ///
    /// IV INTEGRITY
    /// ────────────
    /// A fresh 16-byte IV is generated via <see cref="RandomNumberGenerator.GetBytes"/>
    /// (CSPRNG) for every file encryption call.  The IV is prepended to the output
    /// file as the first 16 bytes so decryption recovers it without any extra storage.
    /// </summary>
    public class DppEncryptionService
    {
        private const int IvSizeBytes = 16; // AES block size
        private const int AesKeySizeBytes = 32; // AES-256 = 32 bytes
        private readonly EncryptionKeyProvider _keyProvider;

        public DppEncryptionService(EncryptionKeyProvider keyProvider)
        {
            _keyProvider = keyProvider;
        }

        /// <summary>
        /// Encrypts a file using AES-256-CBC with a unique per-document key.
        /// Returns the encrypted file path AND the AES key (Base64) so it can be stored encrypted.
        /// </summary>
        public async Task<FileEncryptionResult> EncryptFileWithUniqueKeyAsync(IFormFile file, string outputPath)
        {
            // Generate unique AES-256 key for this document
            var aesKey = RandomNumberGenerator.GetBytes(AesKeySizeBytes);
            var iv     = RandomNumberGenerator.GetBytes(IvSizeBytes);

            using var aes = Aes.Create();
            aes.Key     = aesKey;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var outputStream = new FileStream(outputPath, FileMode.Create);

            // Prepend IV as the first 16 bytes
            await outputStream.WriteAsync(iv, 0, iv.Length);

            using var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using var inputStream  = file.OpenReadStream();
            await inputStream.CopyToAsync(cryptoStream);

            // Encrypt the AES key with RSA-2048 for storage
            var encryptedAesKey = _keyProvider.EncryptAesKeyWithRsa(aesKey);

            return new FileEncryptionResult
            {
                EncryptedFilePath = outputPath,
                EncryptedAesKey   = encryptedAesKey,  // RSA-encrypted, Base64
                PlaintextAesKey   = Convert.ToBase64String(aesKey),  // For access grants
                Algorithm         = "AES-256-CBC + RSA-2048"
            };
        }

        /// <summary>
        /// Decrypts a file using the provided AES key (Base64).
        /// Used by exporters who have been granted access.
        /// </summary>
        public async Task<Stream> DecryptFileWithKeyAsync(string encryptedFilePath, string aesKeyBase64)
        {
            if (!File.Exists(encryptedFilePath))
                throw new FileNotFoundException("Encrypted file not found", encryptedFilePath);

            var aesKey = Convert.FromBase64String(aesKeyBase64);

            using var fileStream = new FileStream(encryptedFilePath, FileMode.Open, FileAccess.Read);

            var iv         = new byte[IvSizeBytes];
            var bytesRead  = await fileStream.ReadAsync(iv, 0, iv.Length);
            if (bytesRead < IvSizeBytes)
                throw new InvalidDataException("File too short — IV header missing or corrupt.");

            using var aes = Aes.Create();
            aes.Key     = aesKey;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var memoryStream = new MemoryStream();
            using var cryptoStream = new CryptoStream(fileStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
            await cryptoStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            return memoryStream;
        }

        /// <summary>
        /// Decrypts a file using the RSA-encrypted AES key stored in DppDocument.
        /// Requires RSA private key from EncryptionKeyProvider.
        /// </summary>
        public async Task<Stream> DecryptFileAsync(string encryptedFilePath, string encryptedAesKeyBase64)
        {
            var aesKey = _keyProvider.DecryptAesKeyWithRsa(encryptedAesKeyBase64);
            return await DecryptFileWithKeyAsync(encryptedFilePath, Convert.ToBase64String(aesKey));
        }
    }

    /// <summary>
    /// Result of hybrid encryption operation
    /// </summary>
    public class FileEncryptionResult
    {
        public string EncryptedFilePath { get; set; } = string.Empty;
        public string EncryptedAesKey { get; set; } = string.Empty;   // RSA-encrypted (Base64)
        public string PlaintextAesKey { get; set; } = string.Empty;   // Plaintext (Base64) — for grants
        public string Algorithm { get; set; } = string.Empty;
    }
}
