using System.Security.Cryptography;
using System.Text;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    public class DppEncryptionService
    {
        // For production, these should be loaded from Environment Variables / Key Vault
        // 32 chars = 256 bits
        private readonly byte[] _key = Encoding.UTF8.GetBytes("RubberIntelligenceDppSecretKey!3"); 
        // 16 chars = 128 bits
        private readonly byte[] _iv = Encoding.UTF8.GetBytes("RubberIntelIV123");

        public DppEncryptionService(IConfiguration config)
        {
            var keyEnv = Environment.GetEnvironmentVariable("DPP_ENCRYPTION_KEY");
            if (!string.IsNullOrEmpty(keyEnv))
            {
                // Ensure key is 32 bytes
                var keyBytes = Encoding.UTF8.GetBytes(keyEnv);
                if (keyBytes.Length == 32) _key = keyBytes;
            }
        }

        public async Task<string> EncryptFileAsync(IFormFile file, string outputPath)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;

            using var fileStream = new FileStream(outputPath, FileMode.Create);
            using var cryptoStream = new CryptoStream(fileStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
            using var originalStream = file.OpenReadStream();

            await originalStream.CopyToAsync(cryptoStream);

            return outputPath;
        }

        public async Task<Stream> DecryptFileAsync(string inputPath)
        {
            if (!File.Exists(inputPath)) throw new FileNotFoundException("File not found");

            var memoryStream = new MemoryStream();
            
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;

            using var fileStream = new FileStream(inputPath, FileMode.Open);
            using var cryptoStream = new CryptoStream(fileStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
            
            await cryptoStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            
            return memoryStream;
        }
    }
}
