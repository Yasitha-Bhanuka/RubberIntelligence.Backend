using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RubberIntelligence.API.Data;
using RubberIntelligence.API.Modules.Bidding.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

//talk directly to the MongoDB database.

namespace RubberIntelligence.API.Modules.Bidding.Services
{
    public class BiddingRepository : IBiddingRepository
    {
        private readonly IMongoCollection<Auction> _auctions;
        private readonly IMongoCollection<Bid> _bids;

        public BiddingRepository(IOptions<MongoDbSettings> settings)
        {
            var mongoClient = new MongoClient(settings.Value.ConnectionString);
            var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
            
            _auctions = mongoDatabase.GetCollection<Auction>("Auctions");
            _bids = mongoDatabase.GetCollection<Bid>("Bids");
        }

        public async Task<Auction?> GetAuctionByIdAsync(string id)
        {
            if (!ObjectId.TryParse(id, out _)) return null;
            return await _auctions.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        public async Task<List<Auction>> GetActiveAuctionsAsync()
        {
            return await _auctions.Find(x => x.Status == "Active").SortByDescending(x => x.CreatedAt).ToListAsync();
        }

        public async Task<List<Auction>> GetClosedAuctionsAsync()
        {
            return await _auctions.Find(x => x.Status == "Closed").SortByDescending(x => x.UpdatedAt).ToListAsync();
        }

        public async Task CreateAuctionAsync(Auction auction)
        {
            await _auctions.InsertOneAsync(auction);
        }

        public async Task UpdateAuctionAsync(Auction auction)
        {
            await _auctions.ReplaceOneAsync(x => x.Id == auction.Id, auction);
        }

        public async Task<Bid?> GetBidByIdAsync(string id)
        {
            if (!ObjectId.TryParse(id, out _)) return null;
            return await _bids.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        public async Task<List<Bid>> GetBidsForAuctionAsync(string auctionId)
        {
            return await _bids.Find(x => x.AuctionId == auctionId).SortByDescending(x => x.Timestamp).ToListAsync();
        }

        public async Task CreateBidAsync(Bid bid)
        {
            await _bids.InsertOneAsync(bid);
        }
    }
}
