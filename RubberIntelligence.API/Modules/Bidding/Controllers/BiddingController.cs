using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Modules.Bidding.DTOs;
using RubberIntelligence.API.Modules.Bidding.Services;
using System.Security.Claims;
using System.Threading.Tasks;

namespace RubberIntelligence.API.Modules.Bidding.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BiddingController : ControllerBase
    {
        private readonly IBiddingService _biddingService;

        public BiddingController(IBiddingService biddingService)
        {
            _biddingService = biddingService;
        }

        [HttpGet("auctions")]
        public async Task<IActionResult> GetActiveAuctions()
        {
            var auctions = await _biddingService.GetActiveAuctionsAsync();
            return Ok(new { success = true, data = auctions });
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetClosedAuctions()
        {
            var auctions = await _biddingService.GetClosedAuctionsAsync();
            return Ok(new { success = true, data = auctions });
        }

        [HttpGet("auctions/{id}")]
        public async Task<IActionResult> GetAuctionDetails(string id)
        {
            var auction = await _biddingService.GetAuctionDetailsAsync(id);
            if (auction == null)
            {
                return NotFound(new { success = false, message = "Auction not found" });
            }
            return Ok(new { success = true, data = auction });
        }

        [HttpPost("auctions")]
        public async Task<IActionResult> CreateAuction([FromBody] CreateAuctionDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "Unknown User";

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { success = false, message = "User not found" });
            }

            var auction = await _biddingService.CreateAuctionAsync(dto, userId, userName);
            return Ok(new { success = true, data = auction });
        }

        [HttpPost("auctions/{id}/bid")]
        public async Task<IActionResult> PlaceBid(string id, [FromBody] CreateBidDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var userName = User.FindFirstValue(ClaimTypes.Name) ?? "Unknown User";
                var userRole = User.FindFirstValue(ClaimTypes.Role) ?? "Unknown Role";

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "User not found" });
                }

                var success = await _biddingService.PlaceBidAsync(id, dto, userId, userName, userRole);

                if (!success)
                {
                    return BadRequest(new { success = false, message = "Failed to place bid. Auction may be closed or invalid." });
                }

                return Ok(new { success = true, message = "Bid placed successfully" });
            }
            catch (System.InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (System.Exception ex)
            {
                // In a real app we would log this ex
                return StatusCode(500, new { success = false, message = "An error occurred while placing the bid." });
            }
        }
    }
}
