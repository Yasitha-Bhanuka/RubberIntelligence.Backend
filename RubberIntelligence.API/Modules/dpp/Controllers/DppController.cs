using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using RubberIntelligence.API.Data.Repositories;
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
        private readonly IDppRepository _dppRepository;
        private readonly IUserRepository _userRepository;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<DppController> _logger;

        public DppController(
            GeminiOcrService ocrService,
            OnnxDppService onnxDppService,
            DppDocumentProcessingService processingService,
            DppService dppService,
            IDppRepository dppRepository,
            IUserRepository userRepository,
            IWebHostEnvironment env,
            ILogger<DppController> logger)
        {
            _ocrService                = ocrService;
            _onnxDppService            = onnxDppService;
            _processingService         = processingService;
            _dppService                = dppService;
            _dppRepository             = dppRepository;
            _userRepository            = userRepository;
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

            // 6. Save DppDocument with pre-generated Id and public-only summary.
            //    Confidential field values are structurally absent from this record.
            var dppDoc = new DppDocument
            {
                Id                   = docId,
                OriginalFileName     = request.File.FileName,
                StoredFilePath       = tempPath,
                ContentType          = request.File.ContentType,
                Classification       = classificationResult.Classification,
                ConfidenceScore      = classificationResult.ConfidenceScore,
                UploadedAt           = DateTime.UtcNow,
                UploadedBy           = userId,
                // SAFE: only non-confidential field values — confidential plaintext never stored here
                ExtractedTextSummary = string.IsNullOrWhiteSpace(safeSummary) ? null : safeSummary,
                DetectedKeywords     = classificationResult.InfluentialKeywords
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
                // Supported file types info for the client
                supportedFormats = new[] { "image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf" }
            });
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
