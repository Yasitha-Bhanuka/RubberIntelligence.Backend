using System.Security.Cryptography;
using System.Text;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// AES-256-CBC encryption for individual field values.
    /// Generates a random IV per encryption — never reuses IV.
    /// </summary>
    public class FieldEncryptionService
    {
        private readonly byte[] _key;

        public FieldEncryptionService(IConfiguration config)
        {
            // Load key from environment variable or fallback to hardcoded (32 bytes = 256 bits)
            var keyString = Environment.GetEnvironmentVariable("DPP_FIELD_ENCRYPTION_KEY")
                            ?? "RubberDppFieldEncryptKey256Bit!!"; // exactly 32 chars

            _key = Encoding.UTF8.GetBytes(keyString);

            if (_key.Length != 32)
                throw new InvalidOperationException("DPP_FIELD_ENCRYPTION_KEY must be exactly 32 characters (256 bits).");
        }

        /// <summary>
        /// Encrypts a plaintext string using AES-256-CBC with a random IV.
        /// Returns EncryptedValue (Base64) and IV (Base64).
        /// </summary>
        public EncryptionResult Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV(); // Random IV every time

            var plainBytes = Encoding.UTF8.GetBytes(plainText);

            using var encryptor = aes.CreateEncryptor();
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return new EncryptionResult
            {
                EncryptedValue = Convert.ToBase64String(encryptedBytes),
                IV = Convert.ToBase64String(aes.IV)
            };
        }

        /// <summary>
        /// Decrypts an AES-256-CBC encrypted value using the stored IV.
        /// </summary>
        public string Decrypt(string encryptedValue, string iv)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = Convert.FromBase64String(iv);
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var encryptedBytes = Convert.FromBase64String(encryptedValue);

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

            return Encoding.UTF8.GetString(plainBytes);
        }
    }

    /// <summary>
    /// Holds the result of a field encryption operation.
    /// </summary>
    public class EncryptionResult
    {
        public string EncryptedValue { get; set; } = string.Empty;
        public string IV { get; set; } = string.Empty;
    }
}
