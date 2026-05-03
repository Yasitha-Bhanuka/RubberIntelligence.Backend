using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Modules.Marketplace.Models;
using RubberIntelligence.API.Modules.Marketplace.Services;
using RubberIntelligence.API.Modules.Marketplace.DTOs;
using RubberIntelligence.API.Modules.Dpp.Services;
using RubberIntelligence.API.Modules.Dpp.DTOs;
using RubberIntelligence.API.Domain.Entities;
using System.Security.Claims;
using System.Text.Json;

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
        private readonly IDppRepository _dppRepository;
        private readonly FieldEncryptionService _fieldEncryptionService;
        private readonly ZeroKnowledgeEncryptionService _zkEncryptionService;
        private readonly DppService _dppService;

        public MarketplaceController(
            IMarketplaceRepository marketplaceRepository,
            IUserRepository userRepository,
            OnnxDppService onnxDppService,
            GeminiOcrService geminiOcrService,
            BuyerHistoryService buyerHistoryService,
            DppDocumentProcessingService processingService,
            IWebHostEnvironment env,
            ILogger<MarketplaceController> logger,
            IDppRepository dppRepository,
            FieldEncryptionService fieldEncryptionService,
            ZeroKnowledgeEncryptionService zkEncryptionService,
            DppService dppService)
        {
            _marketplaceRepository  = marketplaceRepository;
            _userRepository         = userRepository;
            _onnxDppService         = onnxDppService;
            _geminiOcrService       = geminiOcrService;
            _buyerHistoryService    = buyerHistoryService;
            _processingService      = processingService;
            _env                    = env;
            _logger                 = logger;
            _dppRepository          = dppRepository;
            _fieldEncryptionService = fieldEncryptionService;
            _zkEncryptionService    = zkEncryptionService;
            _dppService             = dppService;
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

            if (string.IsNullOrWhiteSpace(exporterId))
                return Unauthorized(new { error = "Invalid token: missing exporter id claim." });

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
                LastUpdatedAt = DateTime.UtcNow,
                SecretRequestId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLower()
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
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(new { error = "Invalid token: missing user id claim." });

            try
            {
                var transactions = await _marketplaceRepository.GetTransactionsByUserIdAsync(userId);
                return Ok(transactions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Marketplace] Failed to fetch transactions for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to fetch transactions." });
            }
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

            // 4b. Persist encrypted field records to MongoDB so GetInvoiceFields can retrieve them.
            await _dppRepository.SaveExtractedFieldsAsync(fieldRecords);

            // Build safe display values (null for confidential — plaintext never stored)
            var safeInvoiceFields = fieldRecords
                .ToDictionary(
                    f => f.FieldName,
                    f => (string?)(f.IsConfidential ? null : f.EncryptedValue));

            // Build plaintext for ONNX classifier using public values only
            var classifyText = string.Join(" ", fieldRecords
                .Where(f => !f.IsConfidential && !string.IsNullOrWhiteSpace(f.EncryptedValue))
                .Select(f => f.EncryptedValue));

            // 5. Classify document + conditionally encrypt the file
            //    NON_CONFIDENTIAL → plain file saved, EncryptionMetadata = null
            //    CONFIDENTIAL     → AES-256-CBC encrypted .enc file, EncryptionMetadata = JSON
            string storagePath = Path.Combine(_env.ContentRootPath, "App_Data", "SecureInvoices");
            string storedPath, dppClassification;
            string? encryptionMetadata;
            try
            {
                using var encStream = file.OpenReadStream();
                (storedPath, dppClassification, encryptionMetadata) =
                    await _onnxDppService.ProcessAndSecureInvoiceAsync(
                        encStream, file.FileName, classifyText, transaction.ExporterId, storagePath);
            }
            finally
            {
                classifyText = string.Empty; // Eagerly clear — values may contain PII
            }

            // 6a. Conditional Zero-Knowledge Vault — PBKDF2-AES-256-CBC file-level encryption
            //     Password = transaction.SecretRequestId  (known only to the exporter)
            //     Salt     = transaction.Id               (lot-specific)
            if (!string.IsNullOrEmpty(transaction.SecretRequestId))
            {
                byte[] rawFileBytes;
                using (var rawStream = file.OpenReadStream())
                using (var ms = new System.IO.MemoryStream())
                {
                    await rawStream.CopyToAsync(ms);
                    rawFileBytes = ms.ToArray();
                }

                if (dppClassification == "CONFIDENTIAL")
                {
                    // Encrypt with PBKDF2(SecretRequestId, transactionId) → AES-256-CBC
                    var (cipherB64, ivB64) = _zkEncryptionService.EncryptDocumentBankStatement(
                        rawFileBytes, transaction.SecretRequestId, transaction.Id);
                    transaction.ConditionalVault   = cipherB64;
                    transaction.ConditionalVaultIv = ivB64;
                    _logger.LogInformation(
                        "[Invoice] CONFIDENTIAL vault encrypted with PBKDF2 for transaction {TxId}", id);
                }
                else
                {
                    // PUBLIC — store raw bytes as Base64; no key needed to read
                    transaction.ConditionalVault   = Convert.ToBase64String(rawFileBytes);
                    transaction.ConditionalVaultIv = string.Empty;
                    _logger.LogInformation(
                        "[Invoice] PUBLIC vault stored as Base64 for transaction {TxId}", id);
                }
            }
            else
            {
                _logger.LogWarning(
                    "[Invoice] No SecretRequestId on transaction {TxId} — ConditionalVault skipped.\n" +
                    "This transaction was created before the zero-knowledge workflow was activated.", id);
            }

            // 6b. Zero-Knowledge guarantee: permanently nullify the SecretRequestId.
            transaction.SecretRequestId = null;

            // 6c. Persist to MongoDB
            //     EncryptionMetadata is null for NON_CONFIDENTIAL documents (plain file).
            //     GetInvoice checks for null to decide whether to decrypt.
            transaction.DppInvoicePath     = storedPath;
            transaction.DppClassification  = dppClassification;
            transaction.EncryptionMetadata = encryptionMetadata;   // null → not encrypted
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

        // ── GET /api/Marketplace/transactions/{id}/invoice-fields ────────────
        /// <summary>
        /// Returns the extracted invoice fields for a transaction, decrypting
        /// confidential values on demand. Only the Buyer who owns the transaction
        /// may call this endpoint.
        /// </summary>
        [Authorize(Roles = "Buyer,Admin")]
        [HttpGet("transactions/{id}/invoice-fields")]
        public async Task<IActionResult> GetInvoiceFields(string id)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentUserId)) return Unauthorized();

            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null) return NotFound("Transaction not found.");

            // Ownership check: only the buyer who uploaded the invoice may read fields
            if (transaction.BuyerId != currentUserId)
                return StatusCode(403, new { error = "Access denied: only the invoice owner may view extracted fields." });

            if (transaction.InvoiceFields == null || transaction.InvoiceFields.Count == 0)
                return NotFound(new { error = "No extracted fields found for this invoice." });

            try
            {
                var storedFields = await _dppRepository.GetExtractedFieldsByLotIdAsync(id);
                if (storedFields.Count == 0)
                    return NotFound(new { error = "Invoice field records have not been stored for this transaction." });

                var result = storedFields.Select(f =>
                {
                    string? plainValue;
                    if (f.IsConfidential && !string.IsNullOrEmpty(f.IV))
                    {
                        try   { plainValue = _fieldEncryptionService.Decrypt(f.EncryptedValue, f.IV); }
                        catch { plainValue = null; } // Decryption failure — surface as null, never log the value
                    }
                    else
                    {
                        // Non-confidential fields are stored as plaintext in EncryptedValue
                        plainValue = string.IsNullOrEmpty(f.EncryptedValue) ? null : f.EncryptedValue;
                    }

                    return new InvoiceFieldDecryptedDto
                    {
                        FieldName      = f.FieldName,
                        Value          = plainValue,
                        IsConfidential = f.IsConfidential
                    };
                })
                // Public fields first, then confidential
                .OrderBy(f => f.IsConfidential ? 1 : 0)
                .ThenBy(f => f.FieldName)
                .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt invoice fields for transaction {TransactionId}", id);
                return StatusCode(500, new { error = "Unable to retrieve invoice fields. Please try again." });
            }
        }

        [Authorize(Roles = "Exporter")]
        [HttpGet("transactions/{id}/invoice")]
        public async Task<IActionResult> GetInvoice(string id)
        {
            var exporterId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            // 1. Validate transaction ownership
            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null) return NotFound("Transaction not found.");
            if (transaction.ExporterId != exporterId)
                return StatusCode(403, "Access Denied: Only the purchasing Exporter can view this invoice.");

            // DppInvoicePath is required; EncryptionMetadata is nullable (null = plain file)
            if (string.IsNullOrEmpty(transaction.DppInvoicePath))
                return NotFound("No invoice available for this transaction.");

            // 2. Route serves BOTH classifications:
            //    CONFIDENTIAL     → RetrieveInvoiceAsync decrypts the .enc file using RSA/AES
            //    NON_CONFIDENTIAL → RetrieveInvoiceAsync streams raw bytes (EncryptionMetadata is null)

            try
            {
                // 3. Retrieve — RetrieveInvoiceAsync handles both encrypted and plain files
                byte[] fileBytes = await _onnxDppService.RetrieveInvoiceAsync(
                    transaction.DppInvoicePath,
                    transaction.EncryptionMetadata,   // null → plain file, no decryption
                    exporterId);

                // 4. Infer content-type from the stored file name
                //    Encrypted files have an extra .enc suffix; plain files keep the original extension.
                var path = transaction.DppInvoicePath;
                string contentType = "application/octet-stream";
                if (path.EndsWith(".pdf.enc") || path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    contentType = "application/pdf";
                else if (path.EndsWith(".jpg.enc") || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                      || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    contentType = "image/jpeg";
                else if (path.EndsWith(".png.enc") || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    contentType = "image/png";

                // Strip .enc from the download file name for encrypted files
                var downloadName = Path.GetFileName(path);
                if (downloadName.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
                    downloadName = Path.GetFileNameWithoutExtension(downloadName);

                _logger.LogInformation(
                    "[Invoice] Served {Classification} document for transaction {TxId} to exporter {ExporterId}",
                    transaction.DppClassification, id, exporterId);

                return File(fileBytes, contentType, downloadName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Invoice] Retrieval failed for transaction {TransactionId}", id);
                return StatusCode(500, new { error = "Document retrieval failed.", details = ex.Message });
            }
        }

        // ==========================================
        // Quality Inspection Report (QIR)
        // ==========================================

        /// <summary>
        /// POST /api/Marketplace/transactions/{id}/qir
        /// Buyer uploads a Quality Inspection Report after the invoice is uploaded.
        /// Gemini extracts QIR-specific fields → per-field confidentiality classification
        /// → AES-256-CBC encryption → HMAC blind index → stored with lotId = "qir_{transactionId}".
        /// </summary>
        [Authorize(Roles = "Buyer")]
        [HttpPost("transactions/{id}/qir")]
        public async Task<IActionResult> UploadQir(string id, IFormFile file)
        {
            var buyerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // 1. Validate file
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file uploaded." });

            // 2. Validate transaction ownership & status
            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null) return NotFound(new { error = "Transaction not found." });
            if (transaction.BuyerId != buyerId) return StatusCode(403, new { error = "Access denied." });
            if (transaction.Status != "InvoiceUploaded")
                return BadRequest(new { error = "Invoice must be uploaded before submitting a Quality Inspection Report." });

            // 3. Gemini structured extraction — QIR schema
            Dictionary<string, string> extractedFields;
            try
            {
                using var ocrStream = file.OpenReadStream();
                extractedFields = await _geminiOcrService.ExtractQirFieldsAsync(ocrStream, file.ContentType);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                return StatusCode(429, new { error = "Gemini API quota exceeded. Please try again later." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QIR] OCR failed for transaction {TransactionId}", id);
                return StatusCode(500, new { error = "OCR processing failed. Check server logs." });
            }

            // 4. Per-field classify & encrypt using the DPP pipeline.
            //    LotId = "qir_{transactionId}" keeps QIR fields separate from invoice fields.
            var qirLotId     = $"qir_{id}";
            var fieldRecords = _processingService.ProcessFields(extractedFields, qirLotId);

            // 4b. Persist encrypted field records to MongoDB (same collection as invoice/DPP fields).
            //     Without this, GetQirFields returns 0 results.
            await _dppRepository.SaveExtractedFieldsAsync(fieldRecords);

            var safeQirFields = fieldRecords
                .ToDictionary(
                    f => f.FieldName,
                    f => (string?)(f.IsConfidential ? null : f.EncryptedValue));

            var classifyText = string.Join(" ", fieldRecords
                .Where(f => !f.IsConfidential && !string.IsNullOrWhiteSpace(f.EncryptedValue))
                .Select(f => f.EncryptedValue));

            // 5. Classify + conditionally secure the QIR file
            //    NON_CONFIDENTIAL → plain file, QirEncryptionMetadata = null
            //    CONFIDENTIAL     → AES-256-CBC + RSA .enc file
            string storagePath = Path.Combine(_env.ContentRootPath, "App_Data", "SecureInvoices");
            string qirPath, qirClassification;
            string? qirEncryptionMetadata;
            try
            {
                using var encStream = file.OpenReadStream();
                (qirPath, qirClassification, qirEncryptionMetadata) =
                    await _onnxDppService.ProcessAndSecureInvoiceAsync(
                        encStream, file.FileName, classifyText, transaction.ExporterId, storagePath);
            }
            finally
            {
                classifyText = string.Empty;
            }

            // 6. Persist QIR data; advance status to QirUploaded
            //    QirEncryptionMetadata is null for NON_CONFIDENTIAL documents (plain file).
            transaction.QirPath               = qirPath;
            transaction.QirClassification     = qirClassification;
            transaction.QirEncryptionMetadata = qirEncryptionMetadata;   // null → not encrypted
            transaction.QirFields             = safeQirFields;
            transaction.Status                = "QirUploaded";
            transaction.LastUpdatedAt         = DateTime.UtcNow;
            await _marketplaceRepository.UpdateTransactionAsync(transaction);

            // 7. Build classification summary
            var classResult = _onnxDppService.ClassifyDocument(
                string.Join(" ", safeQirFields.Values.Where(v => v != null)),
                file.FileName);

            var publicCount       = fieldRecords.Count(f => !f.IsConfidential);
            var confidentialCount = fieldRecords.Count(f =>  f.IsConfidential);

            _logger.LogInformation(
                "[QIR] Processed transaction {TransactionId}: {Total} fields ({Public} public, {Confidential} encrypted), class={Class}",
                id, fieldRecords.Count, publicCount, confidentialCount, qirClassification);

            return Ok(new QirUploadResponseDto
            {
                DppId           = id,
                Message         = "Quality Inspection Report uploaded and secured successfully.",
                FieldsExtracted = fieldRecords.Count,
                Fields = fieldRecords.Select(f => new QirFieldSummaryDto
                {
                    FieldName       = f.FieldName,
                    IsConfidential  = f.IsConfidential,
                    ConfidenceScore = f.ConfidenceScore,
                    HasValue        = !string.IsNullOrWhiteSpace(f.EncryptedValue),
                    ExtractedValue  = f.IsConfidential ? null : f.EncryptedValue
                }).ToList(),
                Classification = new QirClassificationDto
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

        /// <summary>
        /// GET /api/Marketplace/transactions/{id}/exporter-dpp
        /// Returns the combined Digital Product Passport for the purchasing exporter.
        /// Built from invoice fields + QIR fields uploaded by the buyer.
        /// Confidential buyer fields are withheld (value = null, isConfidential = true).
        /// Only accessible by the Exporter who purchased this lot.
        /// </summary>
        [Authorize(Roles = "Exporter,Admin")]
        [HttpGet("transactions/{id}/exporter-dpp")]
        public async Task<IActionResult> GetExporterDpp(string id)
        {
            var callerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerId)) return Unauthorized();

            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null)
                return NotFound(new { error = "Transaction not found." });

            if (transaction.ExporterId != callerId && !User.IsInRole("Admin"))
                return StatusCode(403, new { error = "Access denied: only the purchasing exporter may view this DPP." });

            // ── Invoice fields (lotId = transactionId) ──────────────────
            var invoiceStoredFields = await _dppRepository.GetExtractedFieldsByLotIdAsync(id);
            var invoiceFields = invoiceStoredFields
                .OrderBy(f => f.IsConfidential ? 1 : 0)
                .ThenBy(f => f.FieldName)
                .Select(f => new
                {
                    fieldName      = f.FieldName,
                    // Confidential buyer fields are withheld from the exporter
                    value          = f.IsConfidential ? null : (string.IsNullOrEmpty(f.EncryptedValue) ? null : f.EncryptedValue),
                    isConfidential = f.IsConfidential
                })
                .ToList();

            // ── QIR fields (lotId = "qir_{transactionId}") ──────────────
            var qirLotId         = $"qir_{id}";
            var qirStoredFields  = await _dppRepository.GetExtractedFieldsByLotIdAsync(qirLotId);
            var qirFields = qirStoredFields
                .OrderBy(f => f.IsConfidential ? 1 : 0)
                .ThenBy(f => f.FieldName)
                .Select(f => new
                {
                    fieldName      = f.FieldName,
                    value          = f.IsConfidential ? null : (string.IsNullOrEmpty(f.EncryptedValue) ? null : f.EncryptedValue),
                    isConfidential = f.IsConfidential
                })
                .ToList();

            return Ok(new
            {
                transactionId         = transaction.Id,
                status                = transaction.Status,
                exporterName          = transaction.ExporterName ?? "Unknown",
                buyerRef              = transaction.BuyerId?.Length >= 8
                                            ? transaction.BuyerId.Substring(0, 8)
                                            : transaction.BuyerId ?? "N/A",
                offerPrice            = transaction.OfferPrice,
                lastUpdatedAt         = transaction.LastUpdatedAt,
                invoiceClassification = transaction.DppClassification,
                qirClassification     = transaction.QirClassification,
                invoiceFields,
                qirFields,
                generatedAt           = DateTime.UtcNow
            });
        }

        /// <summary>
        /// GET /api/Marketplace/transactions/{id}/qir-fields
        /// Returns decrypted QIR fields for the owning buyer.
        /// Confidential fields (e.g. pricing-linked metrics) are AES-256-CBC decrypted on demand.
        /// </summary>
        [Authorize(Roles = "Buyer,Admin")]
        [HttpGet("transactions/{id}/qir-fields")]
        public async Task<IActionResult> GetQirFields(string id)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentUserId)) return Unauthorized();

            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null) return NotFound("Transaction not found.");

            if (transaction.BuyerId != currentUserId)
                return StatusCode(403, new { error = "Access denied: only the QIR owner may view extracted fields." });

            if (transaction.QirFields == null || transaction.QirFields.Count == 0)
                return NotFound(new { error = "No QIR fields found. Please upload a Quality Inspection Report first." });

            try
            {
                var qirLotId     = $"qir_{id}";
                var storedFields = await _dppRepository.GetExtractedFieldsByLotIdAsync(qirLotId);
                if (storedFields.Count == 0)
                    return NotFound(new { error = "QIR field records have not been stored for this transaction." });

                var result = storedFields.Select(f =>
                {
                    string? plainValue;
                    if (f.IsConfidential && !string.IsNullOrEmpty(f.IV))
                    {
                        try   { plainValue = _fieldEncryptionService.Decrypt(f.EncryptedValue, f.IV); }
                        catch { plainValue = null; }
                    }
                    else
                    {
                        plainValue = string.IsNullOrEmpty(f.EncryptedValue) ? null : f.EncryptedValue;
                    }

                    return new QirFieldDecryptedDto
                    {
                        FieldName      = f.FieldName,
                        Value          = plainValue,
                        IsConfidential = f.IsConfidential
                    };
                })
                .OrderBy(f => f.IsConfidential ? 1 : 0)
                .ThenBy(f => f.FieldName)
                .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt QIR fields for transaction {TransactionId}", id);
                return StatusCode(500, new { error = "Unable to retrieve QIR fields. Please try again." });
            }
        }

        // ==========================================
        // Lot Interest Request Flow
        // ==========================================

        /// <summary>
        /// POST /api/Marketplace/posts/{id}/express-interest
        /// Exporter signals interest in a buyer's rubber lot.
        /// Post status changes to REQUESTED so the buyer gets notified on next dashboard load.
        /// </summary>
        [Authorize(Roles = "Exporter")]
        [HttpPost("posts/{id}/express-interest")]
        public async Task<IActionResult> ExpressInterest(string id)
        {
            var exporterId   = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var exporterName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown Exporter";
            if (string.IsNullOrEmpty(exporterId)) return Unauthorized();

            var post = await _marketplaceRepository.GetPostByIdAsync(id);
            if (post == null) return NotFound(new { error = "Lot not found." });
            if (post.BuyerId == exporterId) return BadRequest(new { error = "You cannot request your own lot." });
            if (post.Status != "Active" && post.Status != "AVAILABLE" && post.Status != "REQUESTED")
                return BadRequest(new { error = "This lot is no longer available." });

            // Prevent duplicate requests
            var existing = await _marketplaceRepository.GetInterestRequestAsync(id, exporterId);
            if (existing != null)
                return Conflict(new { error = "You have already sent a request for this lot." });

            // Fetch exporter profile for trust scoring
            var exporterUser = await _userRepository.GetByIdAsync(new Guid(exporterId));

            var request = new RubberIntelligence.API.Modules.Marketplace.Models.LotInterestRequest
            {
                PostId                   = id,
                ExporterId               = exporterId,
                ExporterName             = exporterName,
                Country                  = exporterUser?.Country,
                OrganizationType         = exporterUser?.OrganizationType,
                IsVerified               = exporterUser?.IsApproved ?? false,
                Status                   = "PENDING",
                RequestedAt              = DateTime.UtcNow
            };
            await _marketplaceRepository.AddInterestRequestAsync(request);

            // Advance post status to REQUESTED so the buyer is notified
            if (post.Status == "Active" || post.Status == "AVAILABLE")
            {
                post.Status = "REQUESTED";
                await _marketplaceRepository.UpdatePostAsync(post);
            }

            _logger.LogInformation("[Interest] Exporter {ExporterId} expressed interest in post {PostId}", exporterId, id);
            return Ok(new { message = "Your request has been sent to the seller.", interestId = request.Id });
        }

        /// <summary>
        /// GET /api/Marketplace/posts/{id}/interested-exporters
        /// Returns trust-scored list of exporters who have requested this lot.
        /// Only accessible by the Buyer who owns the post.
        /// </summary>
        [Authorize(Roles = "Buyer,Admin")]
        [HttpGet("posts/{id}/interested-exporters")]
        public async Task<IActionResult> GetInterestedExporters(string id)
        {
            var buyerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(buyerId)) return Unauthorized();

            var post = await _marketplaceRepository.GetPostByIdAsync(id);
            if (post == null) return NotFound(new { error = "Lot not found." });
            if (post.BuyerId != buyerId && !User.IsInRole("Admin"))
                return StatusCode(403, new { error = "Only the lot owner can view interested exporters." });

            var requests = await _marketplaceRepository.GetInterestRequestsByPostIdAsync(id);
            var result = requests.Select(r => new
            {
                interestId               = r.Id,
                exporterId               = r.ExporterId,
                exporterName             = r.ExporterName,
                country                  = r.Country,
                organizationType         = r.OrganizationType,
                isVerified               = r.IsVerified,
                status                   = r.Status,
                requestedAt              = r.RequestedAt
            });

            return Ok(result);
        }

        /// <summary>
        /// POST /api/Marketplace/posts/{id}/accept-exporter
        /// Buyer accepts one exporter, creating a PendingInvoice transaction.
        /// All other pending requests for this lot are marked REJECTED.
        /// </summary>
        [Authorize(Roles = "Buyer")]
        [HttpPost("posts/{id}/accept-exporter")]
        public async Task<IActionResult> AcceptExporter(string id, [FromBody] Dictionary<string, string> body)
        {
            var buyerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(buyerId)) return Unauthorized();

            if (!body.TryGetValue("exporterId", out var exporterId) || string.IsNullOrEmpty(exporterId))
                return BadRequest(new { error = "exporterId is required." });

            // 1. Validate post ownership
            var post = await _marketplaceRepository.GetPostByIdAsync(id);
            if (post == null) return NotFound(new { error = "Lot not found." });
            if (post.BuyerId != buyerId) return StatusCode(403, new { error = "Only the lot owner can accept a request." });
            if (post.Status != "REQUESTED" && post.Status != "Active" && post.Status != "AVAILABLE")
                return BadRequest(new { error = "This lot is no longer accepting requests." });

            // 2. Find the accepted interest request
            var acceptedRequest = await _marketplaceRepository.GetInterestRequestAsync(id, exporterId);
            if (acceptedRequest == null)
                return NotFound(new { error = "No interest request found from this exporter." });

            // 3. Generate one-time SecretRequestId — given ONLY to the exporter, NEVER stored in plaintext again
            var secretRequestId = Guid.NewGuid().ToString("N"); // 32-char hex, no hyphens

            // 4. Create transaction (SecretRequestId stored here so UploadInvoice can use it for PBKDF2)
            var transaction = new MarketplaceTransaction
            {
                PostId          = post.Id,
                ExporterId      = exporterId,
                ExporterName    = acceptedRequest.ExporterName,
                BuyerId         = buyerId,
                Status          = "PendingInvoice",
                OfferPrice      = (decimal)post.PricePerKg,
                LastUpdatedAt   = DateTime.UtcNow,
                SecretRequestId = secretRequestId
            };
            await _marketplaceRepository.CreateTransactionAsync(transaction);

            // 5. Mark post as Sold/Approved
            post.Status          = "Sold";
            post.SoldToExporterId = exporterId;
            await _marketplaceRepository.UpdatePostAsync(post);

            // 6. Mark accepted request as ACCEPTED and all others as REJECTED
            acceptedRequest.Status = "ACCEPTED";
            await _marketplaceRepository.UpdateInterestRequestAsync(acceptedRequest);

            var allRequests = await _marketplaceRepository.GetInterestRequestsByPostIdAsync(id);
            foreach (var req in allRequests.Where(r => r.Id != acceptedRequest.Id && r.Status == "PENDING"))
            {
                req.Status = "REJECTED";
                await _marketplaceRepository.UpdateInterestRequestAsync(req);
            }

            _logger.LogInformation("[Accept] Buyer {BuyerId} accepted exporter {ExporterId} for post {PostId}, tx {TxId}",
                buyerId, exporterId, id, transaction.Id);

            // Return the transaction WITHOUT the SecretRequestId.
            // The exporter claims it separately via GET /transactions/{id}/my-secret.
            return Ok(new
            {
                transaction.Id,
                transaction.PostId,
                transaction.ExporterId,
                transaction.ExporterName,
                transaction.BuyerId,
                transaction.Status,
                transaction.OfferPrice,
                transaction.LastUpdatedAt
            });
        }

        // ==========================================
        // Exporter Secret Claim (Zero-Knowledge Key Delivery)
        // ==========================================

        /// <summary>
        /// GET /api/Marketplace/transactions/{id}/my-secret
        ///
        /// One-time key delivery: Only the purchasing Exporter (ReBAC) can call this.
        /// Returns the SecretRequestId needed for client-side PBKDF2-AES-256-CBC decryption.
        /// After the Buyer uploads the invoice, the SecretRequestId is permanently nullified
        /// in the database — so the Exporter MUST claim it before or shortly after upload.
        /// If the key has been consumed (nullified post-encryption), returns 410 Gone.
        /// </summary>
        [Authorize(Roles = "Exporter,Admin")]
        [HttpGet("transactions/{id}/my-secret")]
        public async Task<IActionResult> ClaimSecret(string id)
        {
            var callerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerId)) return Unauthorized();

            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null)
                return NotFound(new { error = "Transaction not found." });

            // ReBAC: only the purchasing exporter
            if (transaction.ExporterId != callerId && !User.IsInRole("Admin"))
                return StatusCode(403, new { error = "Access denied: only the purchasing exporter may claim the decryption key." });

            // If SecretRequestId has been nullified (post-encryption), it's gone forever
            if (string.IsNullOrEmpty(transaction.SecretRequestId))
                return StatusCode(410, new { error = "Decryption key has already been consumed after encryption. It cannot be recovered." });

            _logger.LogInformation(
                "[ClaimSecret] Exporter {ExporterId} claimed SecretRequestId for transaction {TxId}",
                callerId, id);

            return Ok(new { secretRequestId = transaction.SecretRequestId });
        }

        /// <summary>
        /// GET /api/Marketplace/posts/my-requests
        /// Returns all posts owned by the authenticated buyer that have REQUESTED status.
        /// Used by the buyer dashboard to surface the notification badge count and
        /// populate the PendingRequests screen.
        /// </summary>
        [Authorize(Roles = "Buyer,Admin")]
        [HttpGet("posts/my-requests")]
        public async Task<IActionResult> GetMyRequestedPosts()
        {
            var buyerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(buyerId)) return Unauthorized();

            var posts = await _marketplaceRepository.GetRequestedPostsByBuyerIdAsync(buyerId);
            return Ok(posts);
        }

        // ==========================================
        // Dual-Layer DPP (Zero-Knowledge Delivery)
        // ==========================================

        /// <summary>
        /// GET /api/Marketplace/transactions/{id}/dual-layer-dpp
        ///
        /// ReBAC: Only the purchasing exporter (JWT sub == ExporterId) may call this.
        ///
        /// Returns:
        ///   publicSummary  — lot metadata + SHA-256 DppHash (safe to display / embed in QR)
        ///   documentStatus — "CONFIDENTIAL" | "PUBLIC" | "NOT_UPLOADED"
        ///   documentPayload — Base64 ciphertext (CONFIDENTIAL) or Base64 plaintext (PUBLIC)
        ///   ivBase64        — AES IV for client-side PBKDF2 verification (informational)
        ///
        /// The SecretRequestId is NEVER included in this response.
        /// </summary>
        [Authorize(Roles = "Exporter,Admin")]
        [HttpGet("transactions/{id}/dual-layer-dpp")]
        public async Task<IActionResult> GetDualLayerDpp(string id)
        {
            var callerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerId)) return Unauthorized();

            // 1. Load transaction
            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null)
                return NotFound(new { error = "Transaction not found." });

            // 2. ReBAC — only the purchasing exporter
            if (transaction.ExporterId != callerId && !User.IsInRole("Admin"))
                return StatusCode(403, new { error = "Access denied: only the purchasing exporter may view this DPP." });

            // 3. Resolve or auto-generate the DPP record for the public summary
            string dppHash = string.Empty;
            string rubberGrade = string.Empty;
            double quantity = 0;

            try
            {
                var dpp = await _dppRepository.GetDppByLotIdAsync(id);
                if (dpp == null)
                {
                    // Auto-generate from extracted invoice fields if available
                    var fields = await _dppRepository.GetExtractedFieldsByLotIdAsync(id);
                    if (fields.Count > 0)
                    {
                        dpp = await _dppService.GenerateDpp(id);
                    }
                }

                if (dpp != null)
                {
                    dppHash    = dpp.DppHash ?? string.Empty;
                    rubberGrade = dpp.RubberGrade ?? string.Empty;
                    quantity   = dpp.Quantity;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DualLayerDpp] Could not resolve DPP for transaction {TxId}", id);
                // Non-fatal: continue with empty hash
            }

            // 4. Build public summary
            var publicSummary = new
            {
                lotId       = id,
                rubberGrade,
                quantity,
                dppHash
            };

            // 5. Determine document status and payload
            var documentStatus  = string.IsNullOrEmpty(transaction.ConditionalVault)
                ? "NOT_UPLOADED"
                : (transaction.DppClassification ?? "PUBLIC");

            return Ok(new
            {
                publicSummary,
                documentStatus,
                documentPayload = transaction.ConditionalVault ?? string.Empty,
                ivBase64        = transaction.ConditionalVaultIv ?? string.Empty
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // POST /api/Marketplace/transactions/{id}/exporter-docs
        // Exporter uploads their own supporting documents after lot confirmation
        // ─────────────────────────────────────────────────────────────────
        [Authorize(Roles = "Exporter")]
        [HttpPost("transactions/{id}/exporter-docs")]
        public async Task<IActionResult> UploadExporterDocs(string id, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided." });

            var callerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerId)) return Unauthorized();

            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);
            if (transaction == null)
                return NotFound(new { error = "Transaction not found." });

            if (transaction.ExporterId != callerId)
                return StatusCode(403, new { error = "Access denied: only the accepted exporter may upload documents for this transaction." });

            // Save file to disk
            var docsDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "ExporterDocs");
            Directory.CreateDirectory(docsDir);
            var ext = Path.GetExtension(file.FileName);
            var storedName = $"{id}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
            var filePath = Path.Combine(docsDir, storedName);

            using (var stream = System.IO.File.Create(filePath))
                await file.CopyToAsync(stream);

            transaction.ExporterDocsPath = filePath;
            transaction.ExporterDocsOriginalName = file.FileName;
            transaction.ExporterDocsUploadedAt = DateTime.UtcNow;
            transaction.LastUpdatedAt = DateTime.UtcNow;
            await _marketplaceRepository.UpdateTransactionAsync(transaction);

            _logger.LogInformation("[ExporterDocs] Exporter {ExporterId} uploaded docs for transaction {TxId}", callerId, id);

            return Ok(new
            {
                message = "Documents uploaded successfully.",
                fileName = file.FileName,
                uploadedAt = transaction.ExporterDocsUploadedAt,
                transactionId = id
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // GET /api/Marketplace/posts/my-interest-requests
        // Returns all interest requests submitted by the calling Exporter
        // ─────────────────────────────────────────────────────────────────
        [Authorize(Roles = "Exporter")]
        [HttpGet("posts/my-interest-requests")]
        public async Task<IActionResult> GetMyInterestRequests()
        {
            var callerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerId)) return Unauthorized();

            var requests = await _marketplaceRepository.GetInterestRequestsByExporterIdAsync(callerId);
            return Ok(requests.Select(r => new
            {
                id = r.Id,
                postId = r.PostId,
                status = r.Status,
                requestedAt = r.RequestedAt
            }));
        }
    }
}
