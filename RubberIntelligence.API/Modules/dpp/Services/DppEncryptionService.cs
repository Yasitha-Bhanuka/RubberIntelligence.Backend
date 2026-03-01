using System.Security.Cryptography;
using System.Text;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// AES-256-CBC file-level encryption service.
    /// Generates a random 16-byte IV per encryption, prepended to the ciphertext.
    /// Decryption reads the IV from the first 16 bytes of the file automatically.
    /// </summary>
    public class DppEncryptionService
    {
        private readonly byte[] _key;

        public DppEncryptionService(IConfiguration config)
        {
            // Prefer environment variable; fallback to appsettings; fallback to default dev key
            var keyString = Environment.GetEnvironmentVariable("DPP_ENCRYPTION_KEY")
                            ?? config["Dpp:EncryptionKey"]
                            ?? "RubberIntelligenceDppSecretKey!3"; // 32 chars = 256 bits

            _key = Encoding.UTF8.GetBytes(keyString);

            if (_key.Length != 32)
                throw new InvalidOperationException(
                    "DPP_ENCRYPTION_KEY must be exactly 32 characters (256-bit AES key).");
        }

        /// <summary>
        /// Encrypts a file using AES-256-CBC with a random IV.
        /// The IV (16 bytes) is prepended to the output file — no need to store it separately.
        /// </summary>
        public async Task<string> EncryptFileAsync(IFormFile file, string outputPath)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV(); // Cryptographically random IV — different every call

            using var outputStream = new FileStream(outputPath, FileMode.Create);

            // Write IV as the first 16 bytes of the file
            await outputStream.WriteAsync(aes.IV, 0, aes.IV.Length);

            using var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using var inputStream = file.OpenReadStream();
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

            // Recover IV from the first 16 bytes
            var iv = new byte[16];
            var bytesRead = await fileStream.ReadAsync(iv, 0, iv.Length);
            if (bytesRead < 16)
                throw new InvalidDataException("File too short — IV header missing or file is corrupt.");

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var memoryStream = new MemoryStream();
            using var cryptoStream = new CryptoStream(fileStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
            await cryptoStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            return memoryStream;
        }
    }
}
