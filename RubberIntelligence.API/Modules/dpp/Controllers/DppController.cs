using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Modules.Dpp.DTOs;
using RubberIntelligence.API.Modules.Dpp.Models;
using RubberIntelligence.API.Modules.Dpp.Services;

namespace RubberIntelligence.API.Modules.Dpp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DppController : ControllerBase
    {
        private readonly GeminiOcrService _ocrService;
        private readonly OnnxDppService _onnxDppService;
        private readonly DppDocumentProcessingService _processingService; // Fix 2: owns classify+encrypt
        private readonly DppService _dppService;
        private readonly IDppRepository _dppRepository;
        private readonly IUserRepository _userRepository;
        private readonly IWebHostEnvironment _env;

        public DppController(
            GeminiOcrService ocrService,
            OnnxDppService onnxDppService,
            DppDocumentProcessingService processingService,
            DppService dppService,
            IDppRepository dppRepository,
            IUserRepository userRepository,
            IWebHostEnvironment env)
        {
            _ocrService        = ocrService;
            _onnxDppService    = onnxDppService;
            _processingService = processingService;
            _dppService        = dppService;
            _dppRepository     = dppRepository;
            _userRepository    = userRepository;
            _env               = env;
        }

        // ── POST /api/dpp/upload ─────────────────────────────────────────
        [Authorize(Roles = "Buyer,Admin")]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument([FromForm] DocumentUploadRequest request)
        {
            if (request.File == null || request.File.Length == 0)
                return BadRequest("No file uploaded.");

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

            // 1. Save temp file
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

            // Fix 1: never expose ExtractedText or raw values in HTTP response
            return Ok(new
            {
                dppId           = dppDoc.Id,
                fieldsExtracted = fieldRecords.Count,
                fields = fieldRecords.Select(f => new
                {
                    f.FieldName,
                    f.IsConfidential,
                    f.ConfidenceScore,
                    hasValue = !string.IsNullOrWhiteSpace(f.EncryptedValue)
                }),
                classification = new
                {
                    classificationResult.Classification,
                    classificationResult.ConfidenceScore,
                    classificationResult.ConfidenceLevel,
                    classificationResult.SystemAction,
                    classificationResult.Explanation,
                    classificationResult.InfluentialKeywords
                    // ExtractedText + IsEncrypted intentionally excluded — may contain confidential values
                }
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

        // FIX #4 — test endpoints removed
    }
}
