using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Infrastructure.Security;
using RubberIntelligence.API.Modules.Dpp.DTOs;
using RubberIntelligence.API.Modules.Dpp.Models;
using RubberIntelligence.API.Modules.Dpp.Services;
using System.Security.Claims;

namespace RubberIntelligence.API.Modules.Dpp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DppController : ControllerBase
    {
        private readonly GeminiOcrService _ocrService;
        private readonly OnnxDppService _onnxDppService;
        private readonly DppDocumentProcessingService _processingService;
        private readonly DppService _dppService;
        private readonly DppEncryptionService _dppEncryptionService;
        private readonly IDppRepository _dppRepository;
        private readonly IDocumentAccessGrantRepository _grantRepository;
        private readonly IUserRepository _userRepository;
        private readonly EncryptionKeyProvider _keyProvider;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<DppController> _logger;

        public DppController(
            GeminiOcrService ocrService,
            OnnxDppService onnxDppService,
            DppDocumentProcessingService processingService,
            DppService dppService,
            DppEncryptionService dppEncryptionService,
            IDppRepository dppRepository,
            IDocumentAccessGrantRepository grantRepository,
            IUserRepository userRepository,
            EncryptionKeyProvider keyProvider,
            IWebHostEnvironment env,
            ILogger<DppController> logger)
        {
            _ocrService                = ocrService;
            _onnxDppService            = onnxDppService;
            _processingService         = processingService;
            _dppService                = dppService;
            _dppEncryptionService      = dppEncryptionService;
            _dppRepository             = dppRepository;
            _grantRepository           = grantRepository;
            _userRepository            = userRepository;
            _keyProvider               = keyProvider;
            _env                       = env;
            _logger                    = logger;
        }

        // ── POST /api/dpp/upload ─────────────────────────────────────────
        [Authorize(Roles = "Buyer,Admin")]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument([FromForm] DocumentUploadRequest request)
        {
            if (request.File == null || request.File.Length == 0)
                return BadRequest("No file uploaded.");

            // 1a. Validate MIME type — Gemini inline_data only accepts these formats
            var allowedMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif", "application/pdf"
            };

            if (!allowedMimeTypes.Contains(request.File.ContentType))
                return StatusCode(415, new
                {
                    error   = "Unsupported file type.",
                    details = $"Received '{request.File.ContentType}'. Allowed types: JPEG, PNG, WEBP, GIF, PDF."
                });

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

            // 1b. Save temp file
            var uploadsFolder = Path.Combine(_env.ContentRootPath, "Uploads", "Dpp");
            Directory.CreateDirectory(uploadsFolder);
            var tempPath = Path.Combine(uploadsFolder, Guid.NewGuid() + Path.GetExtension(request.File.FileName));

            using (var stream = new FileStream(tempPath, FileMode.Create))
                await request.File.CopyToAsync(stream);

            // 2. Gemini structured extraction → Dictionary<string, string>
            Dictionary<string, string> extractedFields;
            try
            {
                using var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read);
                extractedFields = await _ocrService.ExtractFieldsAsync(fileStream, request.File.ContentType);
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    return StatusCode(429, new { error = "Gemini API Quota Exceeded. Please try again later.", details = ex.Message });

                return StatusCode((int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError),
                    new { error = "OCR Service Failed", details = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Internal Server Error during OCR", details = ex.Message });
            }

            // 3. Classify document type using ONNX
            var extractedText = extractedFields.Count > 0
                ? string.Join(" ", extractedFields.Values.Where(v => !string.IsNullOrWhiteSpace(v)))
                : "No readable text found.";

            var classificationResult = _onnxDppService.ClassifyDocument(extractedText, request.File.FileName);

            // 4. Pre-generate the document ID so ProcessFields can reference it before DppDocument is persisted.
            //    This lets us build a safe ExtractedTextSummary from public fields ONLY before writing to MongoDB.
            var docId = ObjectId.GenerateNewId().ToString();

            // 5. Per-field: classify → encrypt (if confidential) — run BEFORE saving DppDocument.
            //    CONSTRAINT: ExtractedTextSummary must never hold confidential plaintext (Constraint §7).
            var fieldRecords = _processingService.ProcessFields(extractedFields, docId);

            // Build safe summary from NON-CONFIDENTIAL fields only.
            // For public fields, EncryptedValue stores the raw extracted text (no encryption applied).
            // For confidential fields, EncryptedValue is the AES-256 ciphertext — never included here.
            var safeSummaryParts = fieldRecords
                .Where(f => !f.IsConfidential && !string.IsNullOrWhiteSpace(f.EncryptedValue))
                .Select(f => f.EncryptedValue);
            var rawSafeSummary = string.Join(" ", safeSummaryParts).Trim();
            var safeSummary = rawSafeSummary.Length > 120
                ? rawSafeSummary[..120] + "\u2026"
                : rawSafeSummary;

            // 6a. Detect whether the majority of fields are confidential.
            //     If so, encrypt the entire document file and discard the plaintext copy.
            int totalFields        = fieldRecords.Count;
            int confidentialCount  = fieldRecords.Count(f => f.IsConfidential);
            bool majorityConfidential = totalFields > 0 && confidentialCount > totalFields / 2;

            string storedFilePath    = tempPath;
            string? encryptedPath    = null;
            string? encryptedAesKey  = null;
            string? plaintextAesKey  = null;
            string? keyAlgorithm     = null;

            if (majorityConfidential)
            {
                // Store encrypted files in a separate secure folder, never in the public Uploads tree.
                var secureFolder = Path.Combine(_env.ContentRootPath, "App_Data", "SecureDocuments");
                Directory.CreateDirectory(secureFolder);
                var encFileName = docId + ".enc";
                encryptedPath   = Path.Combine(secureFolder, encFileName);

                // Re-open the uploaded file and encrypt it with a unique AES key
                using (var uploadStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
                {
                    var rewindable = new FormFile(uploadStream, 0, uploadStream.Length,
                        request.File.Name, request.File.FileName)
                    {
                        Headers     = request.File.Headers,
                        ContentType = request.File.ContentType
                    };
                    
                    var encryptionResult = await _dppEncryptionService.EncryptFileWithUniqueKeyAsync(rewindable, encryptedPath);
                    encryptedAesKey = encryptionResult.EncryptedAesKey;   // RSA-encrypted (for DppDocument)
                    plaintextAesKey = encryptionResult.PlaintextAesKey;   // For future access grants
                    keyAlgorithm    = encryptionResult.Algorithm;
                }

                // Remove the plaintext temp file — the encrypted copy is the only persisted version.
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);

                storedFilePath = encryptedPath;
                _logger.LogInformation(
                    "[DPP] Document {DocId} encrypted ({Conf}/{Total} confidential fields). Algorithm: {Algo}. Encrypted path: {Path}",
                    docId, confidentialCount, totalFields, keyAlgorithm, encryptedPath);
            }

            // 6b. Save DppDocument with pre-generated Id and public-only summary.
            //     Confidential field values are structurally absent from this record.
            var dppDoc = new DppDocument
            {
                Id                   = docId,
                OriginalFileName     = request.File.FileName,
                StoredFilePath       = storedFilePath,
                ContentType          = request.File.ContentType,
                Classification       = classificationResult.Classification,
                ConfidenceScore      = classificationResult.ConfidenceScore,
                UploadedAt           = DateTime.UtcNow,
                UploadedBy           = userId,
                // SAFE: only non-confidential field values — confidential plaintext never stored here
                ExtractedTextSummary = string.IsNullOrWhiteSpace(safeSummary) ? null : safeSummary,
                DetectedKeywords     = classificationResult.InfluentialKeywords,
                IsDocumentEncrypted  = majorityConfidential,
                EncryptedFilePath    = encryptedPath,
                EncryptedAesKey      = encryptedAesKey,          // RSA-encrypted AES key (Base64)
                KeyEncryptionAlgorithm = keyAlgorithm            // "AES-256-CBC + RSA-2048"
            };

            await _dppRepository.CreateAsync(dppDoc);

            // 7. Bulk-save ExtractedField records to MongoDB
            await _dppRepository.SaveExtractedFieldsAsync(fieldRecords);

            // Build safe extracted content: only non-confidential field values are exposed
            var safeExtractedFields = extractedFields
                .Where(kv => fieldRecords.Any(f => f.FieldName == kv.Key && !f.IsConfidential))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            return Ok(new
            {
                dppId           = dppDoc.Id,
                fieldsExtracted = fieldRecords.Count,
                // Fields with extracted values for non-confidential, null for confidential
                fields = fieldRecords.Select(f => new
                {
                    f.FieldName,
                    f.IsConfidential,
                    f.ConfidenceScore,
                    hasValue       = !string.IsNullOrWhiteSpace(f.EncryptedValue),
                    // Only expose plain value for non-confidential fields; confidential values stay encrypted
                    extractedValue = f.IsConfidential ? null : f.EncryptedValue
                }),
                classification = new
                {
                    classificationResult.Classification,
                    classificationResult.ConfidenceScore,
                    classificationResult.ConfidenceLevel,
                    classificationResult.SystemAction,
                    classificationResult.Explanation,
                    classificationResult.InfluentialKeywords,
                    // Only non-confidential text is included — safe to display
                    geminiExtractedCount = extractedFields.Count,
                    publicFieldCount     = safeExtractedFields.Count,
                    confidentialFieldCount = fieldRecords.Count(f => f.IsConfidential)
                },
                // Whether the file was stored encrypted (majority-confidential path)
                documentEncrypted    = majorityConfidential,
                // Supported file types info for the client
                supportedFormats = new[] { "image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf" }
            });
        }

        // ── GET /api/dpp/{id}/encrypted-file-info ────────────────────────────
        /// <summary>
        /// Returns metadata about the AES-256-CBC encrypted file:
        /// algorithm, IV description, file size, storage path, collection name.
        /// Does NOT return file contents — use decrypt-document for that.
        /// </summary>
        [Authorize(Roles = "Buyer,Exporter,Admin")]
        [HttpGet("{id}/encrypted-file-info")]
        public async Task<IActionResult> GetEncryptedFileInfo(string id)
        {
            var doc = await _dppRepository.GetByIdAsync(id);
            if (doc == null)
                return NotFound(new { error = "Document not found." });

            if (!doc.IsDocumentEncrypted || string.IsNullOrEmpty(doc.EncryptedFilePath))
                return BadRequest(new { error = "This document is not stored as an encrypted file." });

            long? sizeBytes = null;
            if (System.IO.File.Exists(doc.EncryptedFilePath))
            {
                var fi = new FileInfo(doc.EncryptedFilePath);
                sizeBytes = fi.Length;
            }

            return Ok(new
            {
                dppId             = doc.Id,
                originalFileName  = doc.OriginalFileName,
                contentType       = doc.ContentType,
                isEncrypted       = true,
                algorithm         = "AES-256-CBC",
                ivDescription     = "16-byte CSPRNG IV prepended to the .enc file header",
                keyManagement     = "EncryptionKeyProvider — env-var DPP_FILE_ENCRYPTION_KEY → appsettings → dev-fallback",
                encryptedFileName = Path.GetFileName(doc.EncryptedFilePath),
                encryptedSizeBytes = sizeBytes,
                encryptedAt       = doc.UploadedAt,
                collection        = "DppDocuments",
                decryptAccess     = "Exporter, Admin only — GET /api/dpp/{id}/decrypt-document"
            });
        }

        // ── GET /api/dpp/{id}/decrypt-document ───────────────────────────────
        /// <summary>
        /// Decrypts the stored AES-256-CBC document and streams it to the caller.
        /// Only available for documents where IsDocumentEncrypted == true.
        /// Restricted to Exporter and Admin roles.
        /// </summary>
        [Authorize(Roles = "Exporter,Admin")]
        [HttpGet("{id}/decrypt-document")]
        public async Task<IActionResult> DecryptDocument(string id)
        {
            var doc = await _dppRepository.GetByIdAsync(id);
            if (doc == null)
                return NotFound(new { error = "Document not found." });

            if (!doc.IsDocumentEncrypted || string.IsNullOrEmpty(doc.EncryptedFilePath))
                return BadRequest(new { error = "This document is not stored as an encrypted file." });

            if (!System.IO.File.Exists(doc.EncryptedFilePath))
                return NotFound(new { error = "Encrypted file missing from server storage." });

            if (string.IsNullOrEmpty(doc.EncryptedAesKey))
                return BadRequest(new { error = "Encryption key not found for this document." });

            try
            {
                // Decrypt using RSA-wrapped AES key
                var decryptedStream = await _dppEncryptionService.DecryptFileAsync(
                    doc.EncryptedFilePath, 
                    doc.EncryptedAesKey);
                    
                return File(decryptedStream, doc.ContentType, doc.OriginalFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DPP] Decryption failed for document {DocId}", id);
                return StatusCode(500, new { error = "Decryption failed.", details = ex.Message });
            }
        }

        // ── POST /api/dpp/{dppId}/generate-passport ──────────────────────
        // FIX #2 & #3 — expose DppService.GenerateDpp via HTTP
        [Authorize(Roles = "Buyer,Admin")]
        [HttpPost("{dppId}/generate-passport")]
        public async Task<IActionResult> GeneratePassport(string dppId)
        {
            try
            {
                var passport = await _dppService.GenerateDpp(dppId);
                return Ok(passport);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to generate DPP", details = ex.Message });
            }
        }

        // ── GET /api/dpp/passport/{dppId} ────────────────────────────────
        // Fix 3: unified naming — route param is dppId everywhere
        [Authorize(Roles = "Buyer,Exporter,Admin")]
        [HttpGet("passport/{dppId}")]
        public async Task<IActionResult> GetPassport(string dppId)
        {
            var passport = await _dppRepository.GetDppByLotIdAsync(dppId);
            if (passport == null)
                return NotFound(new { error = "No passport found. Call POST /{dppId}/generate-passport first." });

            return Ok(passport);
        }

        // ── GET /api/dpp/my-uploads ──────────────────────────────────────
        [Authorize(Roles = "Buyer,Admin")]
        [HttpGet("my-uploads")]
        public async Task<IActionResult> GetMyDocuments()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var docs = await _dppRepository.GetByBuyerIdAsync(userId);
            return Ok(docs);
        }

        // ── GET /api/dpp/{id}/access ─────────────────────────────────────
        [Authorize(Roles = "Exporter,Admin")]
        [HttpGet("{id}/access")]
        public async Task<IActionResult> GetDocumentAccess(string id)
        {
            var doc = await _dppRepository.GetByIdAsync(id);
            if (doc == null) return NotFound("Document not found.");

            if (!System.IO.File.Exists(doc.StoredFilePath))
                return NotFound("Physical file missing.");

            var stream = new FileStream(doc.StoredFilePath, FileMode.Open, FileAccess.Read);
            return File(stream, doc.ContentType, doc.OriginalFileName);
        }

        // ── GET /api/dpp/{id} ────────────────────────────────────────────
        [Authorize(Roles = "Exporter,Admin")]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetDocumentMetadata(string id)
        {
            var doc = await _dppRepository.GetByIdAsync(id);
            if (doc == null) return NotFound("Document not found.");
            return Ok(doc);
        }



        // ── POST /api/dpp/{id}/grant-access ──────────────────────────────
        /// <summary>
        /// Grants an exporter access to decrypt a specific encrypted document.
        /// Creates a DocumentAccessGrant with the plaintext AES key.
        /// Restricted to Buyer (document owner) and Admin.
        /// </summary>
        [Authorize(Roles = "Buyer,Admin")]
        [HttpPost("{id}/grant-access")]
        public async Task<IActionResult> GrantDocumentAccess(string id, [FromBody] GrantAccessRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var doc = await _dppRepository.GetByIdAsync(id);
            if (doc == null)
                return NotFound(new { error = "Document not found." });

            // Verify ownership (buyer can only grant access to their own docs, admin can grant to any)
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "Admin" && doc.UploadedBy != userId)
                return StatusCode(403, new { error = "You can only grant access to your own documents." });

            if (!doc.IsDocumentEncrypted || string.IsNullOrEmpty(doc.EncryptedAesKey))
                return BadRequest(new { error = "This document is not encrypted." });

            // Check if grant already exists
            var existingGrant = await _grantRepository.GetGrantAsync(id, request.ExporterId);
            if (existingGrant != null)
                return BadRequest(new { error = "Access already granted to this exporter." });

            // Decrypt the RSA-wrapped AES key to get plaintext key for the grant
            var decryptedKeyBytes = _keyProvider.DecryptAesKeyWithRsa(doc.EncryptedAesKey);
            var plaintextAesKey = Convert.ToBase64String(decryptedKeyBytes);

            var grant = new DocumentAccessGrant
            {
                Id            = ObjectId.GenerateNewId().ToString(),
                DppDocumentId = id,
                ExporterId    = request.ExporterId,
                DecryptionKey = plaintextAesKey,  // Store plaintext AES key for exporter
                GrantedAt     = DateTime.UtcNow,
                GrantedBy     = userId,
                TransactionId = request.TransactionId
            };

            await _grantRepository.CreateGrantAsync(grant);

            _logger.LogInformation(
                "[DPP] Access granted: Document {DocId} → Exporter {ExporterId} by {GrantedBy}",
                id, request.ExporterId, userId);

            return Ok(new
            {
                message       = "Access granted successfully.",
                grantId       = grant.Id,
                exporterId    = grant.ExporterId,
                documentId    = grant.DppDocumentId,
                grantedAt     = grant.GrantedAt,
                transactionId = grant.TransactionId
            });
        }

        // ── GET /api/dpp/my-granted-documents ────────────────────────────
        /// <summary>
        /// Returns all documents the current exporter has been granted access to.
        /// </summary>
        [Authorize(Roles = "Exporter,Admin")]
        [HttpGet("my-granted-documents")]
        public async Task<IActionResult> GetMyGrantedDocuments()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var grants = await _grantRepository.GetGrantsByExporterIdAsync(userId);
            
            var documents = new List<object>();
            foreach (var grant in grants)
            {
                var doc = await _dppRepository.GetByIdAsync(grant.DppDocumentId);
                if (doc != null)
                {
                    documents.Add(new
                    {
                        grantId          = grant.Id,
                        documentId       = doc.Id,
                        fileName         = doc.OriginalFileName,
                        classification   = doc.Classification,
                        uploadedAt       = doc.UploadedAt,
                        grantedAt        = grant.GrantedAt,
                        transactionId    = grant.TransactionId,
                        isEncrypted      = doc.IsDocumentEncrypted,
                        encryptionAlgo   = doc.KeyEncryptionAlgorithm
                    });
                }
            }

            return Ok(new
            {
                count     = documents.Count,
                documents = documents
            });
        }

        // ── GET /api/dpp/{id}/decrypt-with-grant ─────────────────────────
        /// <summary>
        /// Decrypts a document using an access grant.
        /// Exporters can only decrypt documents they've been granted access to.
        /// </summary>
        [Authorize(Roles = "Exporter,Admin")]
        [HttpGet("{id}/decrypt-with-grant")]
        public async Task<IActionResult> DecryptWithGrant(string id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var doc = await _dppRepository.GetByIdAsync(id);
            if (doc == null)
                return NotFound(new { error = "Document not found." });

            if (!doc.IsDocumentEncrypted || string.IsNullOrEmpty(doc.EncryptedFilePath))
                return BadRequest(new { error = "This document is not encrypted." });

            // Check if exporter has access grant
            var grant = await _grantRepository.GetGrantAsync(id, userId);
            if (grant == null && User.FindFirst(ClaimTypes.Role)?.Value != "Admin")
                return StatusCode(403, new { error = "Access denied. No grant found for this document." });

            try
            {
                // Use the plaintext AES key from the grant (or decrypt for admin)
                string aesKey;
                if (grant != null)
                {
                    aesKey = grant.DecryptionKey;  // Plaintext AES key from grant
                }
                else
                {
                    // Admin can decrypt using RSA private key
                    var keyBytes = _keyProvider.DecryptAesKeyWithRsa(doc.EncryptedAesKey!);
                    aesKey = Convert.ToBase64String(keyBytes);
                }

                var decryptedStream = await _dppEncryptionService.DecryptFileWithKeyAsync(
                    doc.EncryptedFilePath, 
                    aesKey);
                    
                return File(decryptedStream, doc.ContentType, doc.OriginalFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DPP] Decryption with grant failed for document {DocId}", id);
                return StatusCode(500, new { error = "Decryption failed.", details = ex.Message });
            }
        }

        // ── GET /api/dpp/verify/{lotId} ──────────────────────────────────
        /// <summary>
        /// Re-computes the SHA-256 hash over the stored DPP and compares it with
        /// the persisted DppHash. Returns { isValid, recalculatedHash, storedHash }.
        /// </summary>
        [Authorize(Roles = "Buyer,Exporter,Admin")]
        [HttpGet("verify/{lotId}")]
        public async Task<IActionResult> VerifyDpp(string lotId)
        {
            try
            {
                var result = await _dppService.VerifyDppHash(lotId);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { error = $"No DPP found for lot {lotId}." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DPP] Hash verification failed for lot {LotId}", lotId);
                return StatusCode(500, new { error = "Verification failed", details = ex.Message });
            }
        }
    }
}
