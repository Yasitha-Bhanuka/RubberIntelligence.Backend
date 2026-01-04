using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using RubberIntelligence.API.Modules.Dpp.DTOs;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Domain.Entities;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    public class OnnxDppService
    {
        private readonly string _modelPath;
        private readonly ILogger<OnnxDppService> _logger;
        private readonly IUserRepository _userRepository;
        private InferenceSession? _session;
        
        // Mocked or Simplified pipeline dependencies
        // Ideally handled via properly exported ONNX pipeline or Python microservice.
        
        public OnnxDppService(IWebHostEnvironment env, ILogger<OnnxDppService> logger, IUserRepository userRepository)
        {
            _logger = logger;
            _userRepository = userRepository;
            _modelPath = Path.Combine(env.ContentRootPath, "Modules", "Dpp", "Models", "dpp_classifier_model.onnx");
        }

        public ClassificationResultDto ClassifyDocument(string extractedText, string fileName)
        {
            var processedText = PreprocessText(extractedText);
            
            // Hybrid Logic: Fallback to heuristic if ONNX session not ready or for robust validation
            var (isConfidential, confidence, keywords) = AnalyzeContent(processedText);

            var result = new ClassificationResultDto
            {
                FileName = fileName,
                Classification = isConfidential ? "CONFIDENTIAL" : "NON_CONFIDENTIAL",
                ConfidenceScore = confidence,
                ConfidenceLevel = confidence > 0.9 ? "Very High" : (confidence > 0.75 ? "High" : "Moderate"),
                SystemAction = isConfidential ? "ENCRYPT + RESTRICT ACCESS" : "ALLOW NORMAL VIEWING",
                InfluentialKeywords = keywords,
                IsEncrypted = isConfidential,
                Explanation = GenerateExplanation(isConfidential, keywords),
                ExtractedText = extractedText.Length > 100 ? extractedText.Substring(0, 100) + "..." : extractedText
            };

            return result;
        }

        // ==========================================
        // Secure Invoice Processing (Encryption)
        // ==========================================

        public async Task<(string EncryptedFilePath, string DppClassification, string EncryptionMetadata)> ProcessAndSecureInvoiceAsync(
            Stream fileStream, string fileName, string extractedText, string exporterId, string secureStoragePath)
        {
            // 1. Classify
            var classificationResult = ClassifyDocument(extractedText, fileName);
            string classification = classificationResult.Classification;

            // 2. Fetch Exporter to get Public Key
            if (!Guid.TryParse(exporterId, out Guid exporterGuid))
            {
                throw new ArgumentException("Invalid Exporter ID");
            }

            var exporter = await _userRepository.GetByIdAsync(exporterGuid);
            if (exporter == null) throw new Exception("Exporter not found");

            // Ensure Exporter has keys
            if (string.IsNullOrEmpty(exporter.PublicKey) || string.IsNullOrEmpty(exporter.PrivateKey))
            {
                var keys = GenerateRsaKeys();
                exporter.PublicKey = keys.PublicKey;
                exporter.PrivateKey = keys.PrivateKey;
                await _userRepository.UpdateAsync(exporter);
            }

            // 3. Generate Ephemeral AES Key for this file
            using var aes = Aes.Create();
            aes.GenerateKey();
            aes.GenerateIV();
            var aesKey = aes.Key;
            var aesIV = aes.IV;

            // 4. Encrypt File Content with AES (Symmetric)
            string encryptedFileName = $"{Guid.NewGuid()}_{fileName}.enc";
            string fullEncryptedPath = Path.Combine(secureStoragePath, encryptedFileName);

            Directory.CreateDirectory(secureStoragePath);

            using (var outputFileStream = new FileStream(fullEncryptedPath, FileMode.Create))
            using (var cryptoStream = new CryptoStream(outputFileStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                // Reset input stream position just in case
                if (fileStream.CanSeek) fileStream.Position = 0;
                await fileStream.CopyToAsync(cryptoStream);
            }

            // 5. Encrypt AES Key with Exporter's RSA Public Key (Asymmetric)
            var encryptedAesKey = EncryptRsa(aesKey, exporter.PublicKey);

            // 6. Create Metadata
            var metadataObj = new
            {
                IV = Convert.ToBase64String(aesIV),
                EncryptedKey = Convert.ToBase64String(encryptedAesKey)
            };
            string encryptionMetadata = JsonSerializer.Serialize(metadataObj);

            return (fullEncryptedPath, classification, encryptionMetadata);
        }

        public async Task<byte[]> RetrieveInvoiceAsync(string filePath, string encryptionMetadataJson, string accessorId)
        {
            // 1. Fetch User (Accessor) to get Private Key
            if (!Guid.TryParse(accessorId, out Guid accessorGuid)) throw new ArgumentException("Invalid User ID");
            
            var accessor = await _userRepository.GetByIdAsync(accessorGuid);
            if (accessor == null) throw new Exception("User not found");
            
            if (string.IsNullOrEmpty(accessor.PrivateKey)) throw new Exception("User has no decryption keys established.");

            // 2. Parse Metadata
            var metadata = JsonSerializer.Deserialize<EncryptionMetadataDto>(encryptionMetadataJson);
            if (metadata == null) throw new Exception("Invalid encryption metadata");

            byte[] iv = Convert.FromBase64String(metadata.IV);
            byte[] encryptedKey = Convert.FromBase64String(metadata.EncryptedKey);

            // 3. Decrypt AES Key using Private Key
            byte[] aesKey = DecryptRsa(encryptedKey, accessor.PrivateKey);

            // 4. Decrypt File
            if (!File.Exists(filePath)) throw new FileNotFoundException("Encrypted invoice file missing");

            using var aes = Aes.Create();
            aes.Key = aesKey;
            aes.IV = iv;

            using var memoryStream = new MemoryStream();
            using (var fileStream = new FileStream(filePath, FileMode.Open))
            using (var cryptoStream = new CryptoStream(fileStream, aes.CreateDecryptor(), CryptoStreamMode.Read))
            {
                await cryptoStream.CopyToAsync(memoryStream);
            }

            return memoryStream.ToArray();
        }

        // ==========================================
        // Helpers
        // ==========================================

        private (string PublicKey, string PrivateKey) GenerateRsaKeys()
        {
            using var rsa = RSA.Create(2048);
            return (rsa.ToXmlString(false), rsa.ToXmlString(true));
        }

        private byte[] EncryptRsa(byte[] data, string publicKeyXml)
        {
            using var rsa = RSA.Create();
            rsa.FromXmlString(publicKeyXml);
            return rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
        }

        private byte[] DecryptRsa(byte[] data, string privateKeyXml)
        {
            using var rsa = RSA.Create();
            rsa.FromXmlString(privateKeyXml); // Contains private key
            return rsa.Decrypt(data, RSAEncryptionPadding.OaepSHA256);
        }

        private string PreprocessText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.ToLowerInvariant();
            text = text.Replace("lkr", " currency_lkr ").Replace("usd", " currency_usd ");
            text = Regex.Replace(text, @"[^a-z0-9\s\.\,\%]", " ");
            text = text.Replace("currency_lkr", "lkr").Replace("currency_usd", "usd");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private (bool IsConfidential, double Confidence, List<string> Keywords) AnalyzeContent(string text)
        {
            var confidentialTerms = new Dictionary<string, double>
            {
                { "price", 1.5 }, { "amount", 1.2 }, { "invoice", 1.8 }, { "payment", 1.4 },
                { "lkr", 2.0 }, { "usd", 2.0 }, { "bank", 1.3 }, { "account", 1.3 },
                { "confidential", 2.5 }, { "receipt", 1.0 }, { "total", 0.8 }, { "currency", 1.0 },
                { "credit", 1.1 }, { "debit", 1.1 }
            };

            var nonConfidentialTerms = new Dictionary<string, double>
            {
                { "grade", 1.2 }, { "quality", 1.5 }, { "moisture", 1.0 }, { "ash", 1.0 },
                { "certificate", 1.8 }, { "report", 0.5 }, { "test", 0.8 }, { "inspection", 1.0 },
                { "warehouse", 0.9 }, { "batch", 0.7 }, { "weight", 0.5 }, { "sample", 0.6 },
                { "traceability", 1.2 }, { "organic", 1.0 }, { "export", 0.5 }
            };

            double confScore = 0;
            double nonConfScore = 0;
            var foundKeywords = new List<string>();

            foreach (var term in confidentialTerms)
            {
                if (text.Contains(term.Key))
                {
                    confScore += term.Value;
                    foundKeywords.Add(term.Key);
                }
            }

            foreach (var term in nonConfidentialTerms)
            {
                if (text.Contains(term.Key))
                {
                    nonConfScore += term.Value;
                    if (!foundKeywords.Contains(term.Key)) foundKeywords.Add(term.Key);
                }
            }

            double totalScore = confScore - nonConfScore;
            bool isConfidential = totalScore > 0;
            if (text.Contains("confidential")) isConfidential = true;

            double maxPossible = Math.Max(confScore + nonConfScore, 1.0);
            double confidence = 0.5 + (Math.Abs(totalScore) / (maxPossible + 5)) * 0.5;
            confidence = Math.Min(Math.Max(confidence, 0.6), 0.99);

            return (isConfidential, confidence, foundKeywords.Take(5).ToList());
        }

        private string GenerateExplanation(bool isConfidential, List<string> keywords)
        {
            var kwStr = string.Join(", ", keywords);
            return isConfidential 
                ? $"Financial or sensitive information detected. Key indicators found: {kwStr}." 
                : $"Document appears to be a standard operational record (Quality/Logistics). Indicators: {kwStr}.";
        }

        private class EncryptionMetadataDto
        {
            public string IV { get; set; } = "";
            public string EncryptedKey { get; set; } = "";
        }
    }
}
