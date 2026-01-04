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
                Status = "Completed",
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
    }
}
