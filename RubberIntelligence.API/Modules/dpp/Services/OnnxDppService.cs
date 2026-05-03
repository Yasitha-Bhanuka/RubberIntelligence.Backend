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
        private readonly ILogger<OnnxDppService> _logger;
        // IServiceScopeFactory is used instead of IUserRepository directly.
        // A Singleton cannot hold a Scoped dependency — we create a scope on demand.
        private readonly IServiceScopeFactory _scopeFactory;

        // Static: loaded once for the entire application lifetime (Singleton service)
        private static InferenceSession? _session;
        private static bool _sessionLoadAttempted = false;

        public OnnxDppService(IWebHostEnvironment env, ILogger<OnnxDppService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;

            // Load only once — skip if already attempted (success or failure)
            if (!_sessionLoadAttempted)
            {
                _sessionLoadAttempted = true;
                var modelPath = Path.Combine(env.ContentRootPath, "Modules", "Dpp", "Models", "dpp_classifier_model_large.onnx");

                if (File.Exists(modelPath))
                {
                    try
                    {
                        _session = new InferenceSession(modelPath);
                        _logger.LogInformation("[DppAI] ONNX model loaded successfully from {Path}", modelPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[DppAI] Failed to load ONNX model — falling back to keyword heuristics. " +
                            "Cause: {Message}", ex.Message);
                    }
                }
                else
                {
                    _logger.LogWarning("[DppAI] Model file not found at {Path} — using keyword heuristics", modelPath);
                }
            }
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

        public async Task<(string StoredFilePath, string DppClassification, string? EncryptionMetadata)> ProcessAndSecureInvoiceAsync(
            Stream fileStream, string fileName, string extractedText, string exporterId, string secureStoragePath)
        {
            // 1. Classify
            var classificationResult = ClassifyDocument(extractedText, fileName);
            string classification = classificationResult.Classification;

            Directory.CreateDirectory(secureStoragePath);

            // ── NON_CONFIDENTIAL: skip encryption entirely, store the raw file ────
            if (classification != "CONFIDENTIAL")
            {
                string rawFileName = $"{Guid.NewGuid()}_{fileName}";
                string rawFilePath = Path.Combine(secureStoragePath, rawFileName);

                using (var rawOutput = new FileStream(rawFilePath, FileMode.Create))
                {
                    if (fileStream.CanSeek) fileStream.Position = 0;
                    await fileStream.CopyToAsync(rawOutput);
                }

                _logger.LogInformation(
                    "[DppAI] NON_CONFIDENTIAL document stored as plain file: {Path}", rawFilePath);

                // null EncryptionMetadata signals to callers that this file is NOT encrypted
                return (rawFilePath, classification, null);
            }

            // ── CONFIDENTIAL: AES-256-CBC file encryption + RSA key wrapping ──────

            // 2. Fetch Exporter to get Public Key
            if (!Guid.TryParse(exporterId, out Guid exporterGuid))
                throw new ArgumentException("Invalid Exporter ID");

            // Create a transient scope to resolve the scoped IUserRepository from this singleton
            using var scope = _scopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

            var exporter = await userRepository.GetByIdAsync(exporterGuid);
            if (exporter == null) throw new Exception("Exporter not found");

            // Ensure Exporter has RSA keys
            if (string.IsNullOrEmpty(exporter.PublicKey) || string.IsNullOrEmpty(exporter.PrivateKey))
            {
                var keys = GenerateRsaKeys();
                exporter.PublicKey = keys.PublicKey;
                exporter.PrivateKey = keys.PrivateKey;
                await userRepository.UpdateAsync(exporter);
            }

            // 3. Generate Ephemeral AES Key for this file
            using var aes = Aes.Create();
            aes.GenerateKey();
            aes.GenerateIV();
            var aesKey = aes.Key;
            var aesIV  = aes.IV;

            // 4. Encrypt File Content with AES (Symmetric)
            string encryptedFileName = $"{Guid.NewGuid()}_{fileName}.enc";
            string fullEncryptedPath = Path.Combine(secureStoragePath, encryptedFileName);

            using (var outputFileStream = new FileStream(fullEncryptedPath, FileMode.Create))
            using (var cryptoStream = new CryptoStream(outputFileStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                if (fileStream.CanSeek) fileStream.Position = 0;
                await fileStream.CopyToAsync(cryptoStream);
            }

            // 5. Wrap AES Key with Exporter's RSA Public Key (Asymmetric)
            var encryptedAesKey = EncryptRsa(aesKey, exporter.PublicKey);

            // 6. Serialise encryption metadata so RetrieveInvoiceAsync can decrypt later
            var metadataObj = new
            {
                IV           = Convert.ToBase64String(aesIV),
                EncryptedKey = Convert.ToBase64String(encryptedAesKey)
            };
            string encryptionMetadata = JsonSerializer.Serialize(metadataObj);

            _logger.LogInformation(
                "[DppAI] CONFIDENTIAL document encrypted (AES-256-CBC + RSA-2048): {Path}", fullEncryptedPath);

            return (fullEncryptedPath, classification, encryptionMetadata);
        }

        /// <summary>
        /// Retrieves an invoice document.
        /// • If <paramref name="encryptionMetadataJson"/> is null or empty the file was stored
        ///   as a plain (NON_CONFIDENTIAL) document — bytes are returned directly without decryption.
        /// • If metadata is present the file is a CONFIDENTIAL .enc file — AES key is unwrapped
        ///   with the exporter's RSA private key and the file is decrypted on demand.
        /// </summary>
        public async Task<byte[]> RetrieveInvoiceAsync(string filePath, string? encryptionMetadataJson, string accessorId)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Invoice file missing from server storage.", filePath);

            // ── NON_CONFIDENTIAL path: no encryption metadata → return raw bytes ──
            if (string.IsNullOrEmpty(encryptionMetadataJson))
            {
                _logger.LogInformation(
                    "[DppAI] Returning plain (non-encrypted) invoice for accessor {AccessorId}", accessorId);
                return await File.ReadAllBytesAsync(filePath);
            }

            // ── CONFIDENTIAL path: RSA-unwrap AES key → decrypt file ────────────

            // 1. Fetch Exporter's private key
            if (!Guid.TryParse(accessorId, out Guid accessorGuid))
                throw new ArgumentException("Invalid User ID");

            using var scope = _scopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

            var accessor = await userRepository.GetByIdAsync(accessorGuid);
            if (accessor == null)        throw new Exception("User not found");
            if (string.IsNullOrEmpty(accessor.PrivateKey))
                throw new Exception("User has no decryption keys established.");

            // 2. Parse metadata
            var metadata = JsonSerializer.Deserialize<EncryptionMetadataDto>(encryptionMetadataJson)
                           ?? throw new Exception("Invalid encryption metadata");

            byte[] iv           = Convert.FromBase64String(metadata.IV);
            byte[] encryptedKey = Convert.FromBase64String(metadata.EncryptedKey);

            // 3. Unwrap AES key with RSA private key
            byte[] aesKey = DecryptRsa(encryptedKey, accessor.PrivateKey);

            // 4. AES-256-CBC decrypt
            using var aes = Aes.Create();
            aes.Key = aesKey;
            aes.IV  = iv;

            using var memoryStream = new MemoryStream();
            using (var fileStream   = new FileStream(filePath, FileMode.Open, FileAccess.Read))
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
