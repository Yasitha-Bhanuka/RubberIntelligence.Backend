using System.Security.Cryptography;
using System.Text;
using RubberIntelligence.API.Infrastructure.Security;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// AES-256-CBC file-level encryption service.
    ///
    /// KEY MANAGEMENT
    /// ──────────────
    /// Key is sourced exclusively from <see cref="EncryptionKeyProvider"/>.
    /// No hardcoded fallback key is present in this class.
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
        private readonly byte[] _key;

        public DppEncryptionService(EncryptionKeyProvider keyProvider)
        {
            _key = keyProvider.GetFileAesKey();
        }

        /// <summary>
        /// Encrypts a file using AES-256-CBC.
        /// The IV (16 bytes, CSPRNG) is prepended to the output file.
        /// </summary>
        public async Task<string> EncryptFileAsync(IFormFile file, string outputPath)
        {
            // CSPRNG-backed IV — guaranteed unique per file
            var iv = RandomNumberGenerator.GetBytes(IvSizeBytes);

            using var aes = Aes.Create();
            aes.Key     = _key;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var outputStream = new FileStream(outputPath, FileMode.Create);

            // Prepend IV as the first 16 bytes — decryption reads it back automatically
            await outputStream.WriteAsync(iv, 0, iv.Length);

            using var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using var inputStream  = file.OpenReadStream();
            await inputStream.CopyToAsync(cryptoStream);

            return outputPath;
        }

        /// <summary>
        /// Decrypts an AES-256-CBC encrypted file.
        /// Reads the IV from the first 16 bytes automatically.
        /// </summary>
        public async Task<Stream> DecryptFileAsync(string inputPath)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Encrypted file not found", inputPath);

            using var fileStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read);

            var iv         = new byte[IvSizeBytes];
            var bytesRead  = await fileStream.ReadAsync(iv, 0, iv.Length);
            if (bytesRead < IvSizeBytes)
                throw new InvalidDataException(
                    "File too short — IV header missing or file is corrupt.");

            using var aes = Aes.Create();
            aes.Key     = _key;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var memoryStream = new MemoryStream();
            using var cryptoStream = new CryptoStream(fileStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
            await cryptoStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            return memoryStream;
        }
    }
}
