using RubberIntelligence.API.Modules.Bidding.DTOs;
using RubberIntelligence.API.Modules.Bidding.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RubberIntelligence.API.Modules.Bidding.Services
{
    public interface IBiddingService
    {
        Task<List<AuctionDto>> GetActiveAuctionsAsync();
        Task<List<AuctionDto>> GetClosedAuctionsAsync();
        Task<AuctionDto?> GetAuctionDetailsAsync(string id);
        Task<Auction> CreateAuctionAsync(CreateAuctionDto createAuctionDto, string sellerId, string sellerName);
        Task<bool> PlaceBidAsync(string auctionId, CreateBidDto bidDto, string bidderId, string bidderName, string userRole);
    }
}
