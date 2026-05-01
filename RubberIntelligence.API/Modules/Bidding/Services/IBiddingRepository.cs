using RubberIntelligence.API.Modules.Bidding.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RubberIntelligence.API.Modules.Bidding.Services
{//These files don't contain actual logic. They just define the rules.
    public interface IBiddingRepository
    {
        Task<Auction?> GetAuctionByIdAsync(string id);
        Task<List<Auction>> GetActiveAuctionsAsync();
        Task<List<Auction>> GetClosedAuctionsAsync();
        Task CreateAuctionAsync(Auction auction);
        Task UpdateAuctionAsync(Auction auction);
        
        Task<Bid?> GetBidByIdAsync(string id);
        Task<List<Bid>> GetBidsForAuctionAsync(string auctionId);
        Task CreateBidAsync(Bid bid);
    }
}
