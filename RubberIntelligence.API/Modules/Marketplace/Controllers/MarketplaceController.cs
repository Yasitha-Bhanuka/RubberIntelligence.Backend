using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Modules.Marketplace.Models;
using System.Security.Claims;

namespace RubberIntelligence.API.Modules.Marketplace.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MarketplaceController : ControllerBase
    {
        private readonly IMarketplaceRepository _marketplaceRepository;
        private readonly IUserRepository _userRepository;

        public MarketplaceController(IMarketplaceRepository marketplaceRepository, IUserRepository userRepository)
        {
            _marketplaceRepository = marketplaceRepository;
            _userRepository = userRepository;
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

        [Authorize(Roles = "Exporter,Admin")]
        [HttpPost("posts/{postId}/request")]
        public async Task<IActionResult> RequestPurchase(string postId, [FromBody] MarketplaceTransaction request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var post = await _marketplaceRepository.GetPostByIdAsync(postId);
            if (post == null) return NotFound("Post not found");

            // Check if transaction already exists? (Optional logic)

            var transaction = new MarketplaceTransaction
            {
                PostId = postId,
                ExporterId = userId,
                ExporterName = "Rubber Exporter", // TODO Cache name
                BuyerId = post.BuyerId,
                Status = "Pending",
                OfferPrice = request.OfferPrice > 0 ? request.OfferPrice : post.PricePerKg,
                Messages = new List<TransactionMessage>(),
                LastUpdatedAt = DateTime.UtcNow
            };
            
            if (!string.IsNullOrEmpty(request.Messages?.FirstOrDefault()?.Text))
            {
                transaction.Messages.Add(new TransactionMessage 
                { 
                    SenderId = userId, 
                    SenderName = "Exporter", 
                    Text = request.Messages.First().Text,
                    Timestamp = DateTime.UtcNow 
                });
            }

            await _marketplaceRepository.CreateTransactionAsync(transaction);
            return Ok(transaction);
        }

        [Authorize]
        [HttpGet("transactions")]
        public async Task<IActionResult> GetMyTransactions()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var transactions = await _marketplaceRepository.GetTransactionsForUserAsync(userId);
            return Ok(transactions);
        }

        [Authorize]
        [HttpPut("transactions/{id}")]
        public async Task<IActionResult> UpdateTransaction(string id, [FromBody] MarketplaceTransaction update)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var transaction = await _marketplaceRepository.GetTransactionByIdAsync(id);

            if (transaction == null) return NotFound();
            
            // Verify ownership
            if (transaction.BuyerId != userId && transaction.ExporterId != userId) 
                return Forbid();

            // Logic to update status or add message
            if (!string.IsNullOrEmpty(update.Status)) 
                transaction.Status = update.Status;
            
            if (update.Messages != null && update.Messages.Any())
            {
                var newMsg = update.Messages.Last();
                newMsg.SenderId = userId; // Ensure sender is correct
                newMsg.Timestamp = DateTime.UtcNow;
                transaction.Messages.Add(newMsg);
            }
            
            transaction.LastUpdatedAt = DateTime.UtcNow;
            await _marketplaceRepository.UpdateTransactionAsync(transaction);

            return Ok(transaction);
        }
    }
}
