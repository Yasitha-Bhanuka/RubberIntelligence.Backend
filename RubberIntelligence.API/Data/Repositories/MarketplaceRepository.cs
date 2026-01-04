using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RubberIntelligence.API.Data;
using RubberIntelligence.API.Modules.Marketplace.Models;

namespace RubberIntelligence.API.Data.Repositories
{
    public class MarketplaceRepository : IMarketplaceRepository
    {
        private readonly IMongoCollection<SellingPost> _posts;
        private readonly IMongoCollection<MarketplaceTransaction> _transactions;

        public MarketplaceRepository(IOptions<MongoDbSettings> settings)
        {
            var mongoClient = new MongoClient(settings.Value.ConnectionString);
            var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
            _posts = mongoDatabase.GetCollection<SellingPost>("SellingPosts");
            _transactions = mongoDatabase.GetCollection<MarketplaceTransaction>("MarketplaceTransactions");
        }

        // Posts
        public async Task CreatePostAsync(SellingPost post)
        {
            await _posts.InsertOneAsync(post);
        }

        public async Task<List<SellingPost>> GetActivePostsAsync()
        {
            return await _posts.Find(x => x.Status == "Active").SortByDescending(x => x.CreatedAt).ToListAsync();
        }

        public async Task<List<SellingPost>> GetPostsByBuyerIdAsync(string buyerId)
        {
            return await _posts.Find(x => x.BuyerId == buyerId).SortByDescending(x => x.CreatedAt).ToListAsync();
        }

        public async Task<SellingPost?> GetPostByIdAsync(string id)
        {
            return await _posts.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        // Transactions
        public async Task CreateTransactionAsync(MarketplaceTransaction transaction)
        {
            await _transactions.InsertOneAsync(transaction);
        }

        public async Task<MarketplaceTransaction?> GetTransactionByIdAsync(string id)
        {
            return await _transactions.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        public async Task<List<MarketplaceTransaction>> GetTransactionsByUserIdAsync(string userId)
        {
            // Find where user is Buyer OR Exporter
            var filter = Builders<MarketplaceTransaction>.Filter.Or(
                Builders<MarketplaceTransaction>.Filter.Eq(x => x.BuyerId, userId),
                Builders<MarketplaceTransaction>.Filter.Eq(x => x.ExporterId, userId)
            );
            return await _transactions.Find(filter).SortByDescending(x => x.LastUpdatedAt).ToListAsync();
        }

        public async Task UpdateTransactionAsync(MarketplaceTransaction transaction)
        {
            await _transactions.ReplaceOneAsync(x => x.Id == transaction.Id, transaction);
        }

        public async Task UpdatePostAsync(SellingPost post)
        {
            await _posts.ReplaceOneAsync(x => x.Id == post.Id, post);
        }
    }
}
