using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Modules.Marketplace.Models;
using RubberIntelligence.API.Modules.Marketplace.Services;
using RubberIntelligence.API.Modules.Dpp.Services;
using RubberIntelligence.API.Modules.Dpp.DTOs;
using System.Security.Claims;

namespace RubberIntelligence.API.Modules.Marketplace.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MarketplaceController : ControllerBase
    {
        private readonly IMarketplaceRepository _marketplaceRepository;
        private readonly IUserRepository _userRepository;
        private readonly OnnxDppService _onnxDppService;
        private readonly GeminiOcrService _geminiOcrService;
        private readonly BuyerHistoryService _buyerHistoryService;
        private readonly DppDocumentProcessingService _processingService;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<MarketplaceController> _logger;

        public MarketplaceController(
            IMarketplaceRepository marketplaceRepository,
            IUserRepository userRepository,
            OnnxDppService onnxDppService,
            GeminiOcrService geminiOcrService,
            BuyerHistoryService buyerHistoryService,
            DppDocumentProcessingService processingService,
            IWebHostEnvironment env,
            ILogger<MarketplaceController> logger)
        {
            _marketplaceRepository = marketplaceRepository;
            _userRepository        = userRepository;
            _onnxDppService        = onnxDppService;
            _geminiOcrService      = geminiOcrService;
            _buyerHistoryService   = buyerHistoryService;
            _processingService     = processingService;
            _env                   = env;
            _logger                = logger;
        }

        // ==========================================
        // Selling Posts
        // ==========================================

        [Authorize(Roles = "Buyer,Admin")]
        [HttpPost("posts")]
        public async Task<IActionResult> CreatePost([FromBody] SellingPost post)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            
            // Get user details to cache name
            // Assuming we have GetByIdAsync in UserRepository, otherwise we might skip name or fetch it differently
            // but for now let's just use the ID or if we can fetch user:
            // var user = await _userRepository.GetByIdAsync(new Guid(userId)); 
            
            post.BuyerId = userId;
            post.BuyerName = "Rubber Seller"; // TODO: Fetch real name if needed or rely on Frontend to send? Better to fetch here if possible.
            post.CreatedAt = DateTime.UtcNow;
            post.Status = "Active";

            await _marketplaceRepository.CreatePostAsync(post);
            return Ok(post);
        }

        [Authorize] // Buyers can see their own, Exporters can see all
        [HttpGet("posts")]
        public async Task<IActionResult> GetPosts([FromQuery] string? buyerId = null)
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(buyerId))
            {
                var posts = await _marketplaceRepository.GetPostsByBuyerIdAsync(buyerId);
                return Ok(posts);
            }
            
            // If Exporter, return all active
            if (userRole == "Exporter" || userRole == "Admin")
            {
                var posts = await _marketplaceRepository.GetActivePostsAsync();
                return Ok(posts);
            }
            
            // If Buyer, maybe return their own if no specific filter?
            if (userRole == "Buyer" && !string.IsNullOrEmpty(currentUserId))
            {
                 var posts = await _marketplaceRepository.GetPostsByBuyerIdAsync(currentUserId);
                 return Ok(posts);
            }

            return Ok(new List<SellingPost>());
        }

        // ==========================================
        // Transactions
        // ==========================================

        [Authorize(Roles = "Exporter")]
        [HttpPost("posts/{id}/buy")]
        public async Task<IActionResult> BuyItem(string id)
        {
            var exporterId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var exporterName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown Exporter";

            // 1. Get Post
            var post = await _marketplaceRepository.GetPostByIdAsync(id);
            if (post == null) return NotFound("Post not found");
            if (post.Status != "Active") return BadRequest("Item already sold or unavailable");
            if (post.BuyerId == exporterId) return BadRequest("Cannot buy your own item");

            // 2. Create Transaction (Completed)
            var transaction = new MarketplaceTransaction
            {
                PostId = post.Id,
                ExporterId = exporterId,
                ExporterName = exporterName,
                BuyerId = post.BuyerId,
                Status = "PendingInvoice", // Changed from Completed
                OfferPrice = (decimal)post.PricePerKg, // Direct buy at listed price
                LastUpdatedAt = DateTime.UtcNow
            };
            await _marketplaceRepository.CreateTransactionAsync(transaction);

            // 3. Mark Post as Sold
            post.Status = "Sold";
            post.SoldToExporterId = exporterId;
            await _marketplaceRepository.UpdatePostAsync(post);

            return Ok(transaction);
        }

        [Authorize]
        [HttpGet("transactions")]
        public async Task<IActionResult> GetMyTransactions()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var transactions = await _marketplaceRepository.GetTransactionsByUserIdAsync(userId);
            return Ok(transactions);
        }

        // ==========================================
        // Invoice Management (DPP)
        // ==========================================

        [Authorize(Roles = "Buyer")]
        [HttpPost("transactions/{id}/dpp")]
        public async Task<IActionResult> LinkDppDocument(string id, [FromBody] Dictionary<string, string> request)
        {
            var buyerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            
            if (!request.ContainsKey("dppId")) return BadRequest("dppId is required");
            string dppId = request["dppId"];

            // 1. Validate Transaction
            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null) return NotFound("Transaction not found");
            if (transaction.BuyerId != buyerId) return StatusCode(403, "You are not the seller/buyer for this transaction");

            // 2. Update Transaction
            transaction.DppDocumentId = dppId;
            transaction.LastUpdatedAt = DateTime.UtcNow;

            await _marketplaceRepository.UpdateTransactionAsync(transaction);

            return Ok(new { Message = "DPP Document linked successfully" });
        }

        [Authorize(Roles = "Buyer")]
        [HttpPost("transactions/{id}/invoice")]
        public async Task<IActionResult> UploadInvoice(string id, IFormFile file)
        {
            var buyerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            // 1. Validate file
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file uploaded." });

            // 2. Validate transaction ownership
            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null) return NotFound(new { error = "Transaction not found." });
            if (transaction.BuyerId != buyerId) return StatusCode(403, new { error = "Access denied." });

            // 3. Gemini structured extraction — invoice schema; File API used automatically for PDFs
            Dictionary<string, string> extractedFields;
            try
            {
                using var ocrStream = file.OpenReadStream();
                extractedFields = await _geminiOcrService.ExtractInvoiceFieldsAsync(ocrStream, file.ContentType);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                return StatusCode(429, new { error = "Gemini API quota exceeded. Please try again later." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Invoice] OCR failed for transaction {TransactionId}", id);
                return StatusCode(500, new { error = "OCR processing failed. Check server logs." });
            }

            // 4. Per-field classify & encrypt — reuses the same DPP pipeline (AES-256-CBC + HMAC blind index)
            var fieldRecords = _processingService.ProcessFields(extractedFields, id);

            // Build safe display values (null for confidential — plaintext never stored)
            var safeInvoiceFields = fieldRecords
                .ToDictionary(
                    f => f.FieldName,
                    f => (string?)(f.IsConfidential ? null : f.EncryptedValue));

            // Build plaintext for ONNX classifier using public values only
            var classifyText = string.Join(" ", fieldRecords
                .Where(f => !f.IsConfidential && !string.IsNullOrWhiteSpace(f.EncryptedValue))
                .Select(f => f.EncryptedValue));

            // 5. Classify document + AES/RSA-encrypt the raw file (existing invoice security pipeline)
            string storagePath = Path.Combine(_env.ContentRootPath, "App_Data", "SecureInvoices");
            string encryptedPath, dppClassification, encryptionMetadata;
            try
            {
                using var encStream = file.OpenReadStream();
                (encryptedPath, dppClassification, encryptionMetadata) =
                    await _onnxDppService.ProcessAndSecureInvoiceAsync(
                        encStream, file.FileName, classifyText, transaction.ExporterId, storagePath);
            }
            finally
            {
                classifyText = string.Empty; // Eagerly clear — values may contain PII
            }

            // 6. Persist to MongoDB
            transaction.DppInvoicePath     = encryptedPath;
            transaction.DppClassification  = dppClassification;
            transaction.EncryptionMetadata = encryptionMetadata;
            transaction.InvoiceFields      = safeInvoiceFields;
            transaction.Status             = "InvoiceUploaded";
            transaction.LastUpdatedAt      = DateTime.UtcNow;
            await _marketplaceRepository.UpdateTransactionAsync(transaction);

            // 7. Build classification summary from ONNX model
            var classResult = _onnxDppService.ClassifyDocument(
                string.Join(" ", safeInvoiceFields.Values.Where(v => v != null)),
                file.FileName);

            var publicCount      = fieldRecords.Count(f => !f.IsConfidential);
            var confidentialCount = fieldRecords.Count(f => f.IsConfidential);

            _logger.LogInformation(
                "[Invoice] Processed transaction {TransactionId}: {Total} fields ({Public} public, {Confidential} encrypted), class={Class}",
                id, fieldRecords.Count, publicCount, confidentialCount, dppClassification);

            return Ok(new InvoiceUploadResponseDto
            {
                DppId           = id,
                Message         = "Invoice uploaded and secured successfully.",
                FieldsExtracted = fieldRecords.Count,
                Fields = fieldRecords.Select(f => new InvoiceFieldSummaryDto
                {
                    FieldName       = f.FieldName,
                    IsConfidential  = f.IsConfidential,
                    ConfidenceScore = f.ConfidenceScore,
                    HasValue        = !string.IsNullOrWhiteSpace(f.EncryptedValue),
                    ExtractedValue  = f.IsConfidential ? null : f.EncryptedValue
                }).ToList(),
                Classification = new InvoiceClassificationDto
                {
                    Classification         = classResult.Classification,
                    ConfidenceScore        = classResult.ConfidenceScore,
                    ConfidenceLevel        = classResult.ConfidenceLevel,
                    SystemAction           = classResult.SystemAction,
                    Explanation            = classResult.Explanation,
                    InfluentialKeywords    = classResult.InfluentialKeywords,
                    GeminiExtractedCount   = fieldRecords.Count,
                    PublicFieldCount       = publicCount,
                    ConfidentialFieldCount = confidentialCount
                }
            });
        }

        // ── GET /api/marketplace/buyer-history/{buyerId} ─────────────────
        /// <summary>
        /// Returns an exporter-facing summary of the buyer's trading history.
        /// Includes lot counts, quality averages, and DPP verification consistency.
        /// </summary>
        [Authorize(Roles = "Exporter,Admin")]
        [HttpGet("buyer-history/{buyerId}")]
        public async Task<IActionResult> GetBuyerHistory(string buyerId)
        {
            try
            {
                var history = await _buyerHistoryService.GetBuyerHistory(buyerId);
                return Ok(history);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to retrieve buyer history", details = ex.Message });
            }
        }

        [Authorize(Roles = "Exporter")]
        [HttpGet("transactions/{id}/invoice")]
        public async Task<IActionResult> GetInvoice(string id)
        {
            var exporterId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            // 1. Validate
            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null) return NotFound("Transaction not found");
            if (transaction.ExporterId != exporterId) return StatusCode(403, "Access Denied: Only the purchasing Exporter can view this invoice.");

            if (string.IsNullOrEmpty(transaction.DppInvoicePath) || string.IsNullOrEmpty(transaction.EncryptionMetadata))
                return NotFound("No invoice available");

            try
            {
                // 2. Retrieve & Decrypt
                byte[] fileBytes = await _onnxDppService.RetrieveInvoiceAsync(
                    transaction.DppInvoicePath, transaction.EncryptionMetadata, exporterId);

                // Determine content type (default to octet-stream or try to preserve original extension logic if stored)
                // For simplified Prototype: application/octet-stream or assume original type if we stored it. 
                // We didn't store original ContentType in DB. Let's infer from file name or default to PDF/Image.
                string contentType = "application/octet-stream";
                if (transaction.DppInvoicePath.EndsWith(".pdf.enc")) contentType = "application/pdf";
                else if (transaction.DppInvoicePath.EndsWith(".jpg.enc")) contentType = "image/jpeg";
                else if (transaction.DppInvoicePath.EndsWith(".png.enc")) contentType = "image/png";

                return File(fileBytes, contentType, Path.GetFileNameWithoutExtension(transaction.DppInvoicePath)); // Remove .enc
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Decryption Failed: " + ex.Message);
            }
        }
    }
}
