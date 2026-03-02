using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        private readonly ConfidentialAccessService _confidentialAccessService;
        private readonly ExporterContextService _exporterContextService;
        private readonly IDppRepository _dppRepository;
        private readonly IUserRepository _userRepository;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<DppController> _logger;

        public DppController(
            GeminiOcrService ocrService,
            OnnxDppService onnxDppService,
            DppDocumentProcessingService processingService,
            DppService dppService,
            ConfidentialAccessService confidentialAccessService,
            ExporterContextService exporterContextService,
            IDppRepository dppRepository,
            IUserRepository userRepository,
            IWebHostEnvironment env,
            ILogger<DppController> logger)
        {
            _ocrService                = ocrService;
            _onnxDppService            = onnxDppService;
            _processingService         = processingService;
            _dppService                = dppService;
            _confidentialAccessService = confidentialAccessService;
            _exporterContextService    = exporterContextService;
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

            // 4. Save DppDocument record
            var dppDoc = new DppDocument
            {
                OriginalFileName     = request.File.FileName,
                StoredFilePath       = tempPath,
                ContentType          = request.File.ContentType,
                Classification       = classificationResult.Classification,
                ConfidenceScore      = classificationResult.ConfidenceScore,
                UploadedAt           = DateTime.UtcNow,
                UploadedBy           = userId,
                ExtractedTextSummary = classificationResult.ExtractedText,
                DetectedKeywords     = classificationResult.InfluentialKeywords
            };

            await _dppRepository.CreateAsync(dppDoc);

            // 5. Per-field: classify → encrypt (if confidential) — delegated to processing service
            var fieldRecords = _processingService.ProcessFields(extractedFields, dppDoc.Id);

            // 6. Bulk-save ExtractedField records to MongoDB
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

        // ───────────────────────────────────────────────────────────────────────────
        // CONTROLLED ACCESS — AccessRequest workflow
        // ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Exporter submits a request to view confidential fields for a lot.
        /// Status starts as PENDING — buyer must approve it.
        /// </summary>
        [Authorize(Roles = "Exporter")]
        [HttpPost("request-confidential/{lotId}")]
        public async Task<IActionResult> RequestConfidentialAccess(string lotId)
        {
            var exporterId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(exporterId)) return Unauthorized();

            // Verify the DPP exists
            var doc = await _dppRepository.GetByIdAsync(lotId);
            if (doc == null) return NotFound(new { error = "DPP document not found." });

            var request = new AccessRequest
            {
                LotId      = lotId,
                ExporterId = exporterId,
                BuyerId    = doc.UploadedBy,
                Status     = AccessRequestStatus.Pending
            };

            await _dppRepository.CreateAccessRequestAsync(request);
            _logger.LogInformation("[DPP] Exporter {ExporterId} requested confidential access for lot {LotId}", exporterId, lotId);

            return Ok(new { requestId = request.Id, status = request.Status });
        }

        /// <summary>
        /// Buyer approves an exporter's access request.
        /// Only the buyer who owns the DPP can approve.
        /// </summary>
        [Authorize(Roles = "Buyer,Admin")]
        [HttpPost("approve-confidential/{requestId}")]
        public async Task<IActionResult> ApproveConfidentialAccess(string requestId)
        {
            var buyerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(buyerId)) return Unauthorized();

            var request = await _dppRepository.GetAccessRequestAsync(requestId);
            if (request == null) return NotFound(new { error = "Access request not found." });

            // Ensure only the owning buyer can approve
            if (request.BuyerId != buyerId)
                return Forbid();

            if (request.Status != AccessRequestStatus.Pending)
                return BadRequest(new { error = $"Request is already {request.Status}." });

            await _dppRepository.ApproveAccessRequestAsync(requestId);
            _logger.LogInformation("[DPP] Buyer {BuyerId} approved confidential access request {RequestId}", buyerId, requestId);

            return Ok(new { requestId, status = AccessRequestStatus.Approved });
        }

        /// <summary>
        /// Buyer fetches all PENDING access requests for their lots.
        /// </summary>
        [Authorize(Roles = "Buyer,Admin")]
        [HttpGet("pending-requests")]
        public async Task<IActionResult> GetPendingRequests()
        {
            var buyerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(buyerId)) return Unauthorized();

            var requests = await _dppRepository.GetPendingRequestsForBuyerAsync(buyerId);
            return Ok(requests.Select(r => new
            {
                r.Id,
                r.LotId,
                r.ExporterId,
                r.Status,
                r.RequestedAt
            }));
        }

        /// <summary>
        /// Exporter retrieves decrypted confidential fields.
        /// Gate: APPROVED AccessRequest must exist for this exporter + lotId.
        /// Decryption happens ONLY in ConfidentialAccessService — never here.
        /// </summary>
        [Authorize(Roles = "Exporter,Admin")]
        [HttpGet("confidential/{lotId}")]
        public async Task<IActionResult> GetConfidentialFields(string lotId)
        {
            var exporterId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(exporterId)) return Unauthorized();

            // 1. Verify an APPROVED access request exists
            var approvedRequest = await _dppRepository.GetApprovedRequestForLotAndExporterAsync(lotId, exporterId);
            if (approvedRequest == null)
                return StatusCode(403, new { error = "Access denied. Submit POST /api/dpp/request-confidential/{lotId} and await buyer approval." });

            // 2. Fetch only confidential ExtractedFields
            var allFields = await _dppRepository.GetExtractedFieldsByLotIdAsync(lotId);
            var confidentialFields = allFields.Where(f => f.IsConfidential).ToList();

            if (confidentialFields.Count == 0)
                return Ok(new { message = "No confidential fields found for this lot.", fields = Array.Empty<object>() });

            // 3. Delegate decryption to service layer — never decrypt in controller
            var decryptedFields = _confidentialAccessService.DecryptFields(confidentialFields);

            // 4. Log access
            _logger.LogInformation(
                "[DPP] CONFIDENTIAL ACCESS: Exporter {ExporterId} accessed confidential fields for lot {LotId} at {Time}",
                exporterId, lotId, DateTime.UtcNow);

            return Ok(new
            {
                lotId,
                accessGrantedAt = approvedRequest.ApprovedAt,
                fields          = decryptedFields
            });
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

        // ── GET /api/dpp/exporter-context/{exporterId} ───────────────────
        /// <summary>
        /// Returns exporter profile context to the authenticated buyer.
        /// Used in PendingRequestsScreen so buyers can review an exporter before approving.
        /// </summary>
        [Authorize(Roles = "Buyer,Admin")]
        [HttpGet("exporter-context/{exporterId}")]
        public async Task<IActionResult> GetExporterContext(string exporterId)
        {
            var buyerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(buyerId)) return Unauthorized();

            try
            {
                var context = await _exporterContextService.GetExporterContext(exporterId, buyerId);
                return Ok(context);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DPP] ExporterContext fetch failed for {ExporterId}", exporterId);
                return StatusCode(500, new { error = "Failed to retrieve exporter context", details = ex.Message });
            }
        }
    }
}
