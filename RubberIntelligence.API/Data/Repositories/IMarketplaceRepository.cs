using RubberIntelligence.API.Modules.Marketplace.Models;

namespace RubberIntelligence.API.Data.Repositories
{
    public interface IMarketplaceRepository
    {
        // Posts
        Task CreatePostAsync(SellingPost post);
        Task<List<SellingPost>> GetActivePostsAsync();
        Task<List<SellingPost>> GetPostsByBuyerIdAsync(string buyerId);
        Task<SellingPost?> GetPostByIdAsync(string id);
        
        // Transactions
        Task CreateTransactionAsync(MarketplaceTransaction transaction);
        Task<MarketplaceTransaction?> GetTransactionByIdAsync(string id);
        Task<List<MarketplaceTransaction>> GetTransactionsForUserAsync(string userId); // Combined for Buyer/Exporter
        Task UpdateTransactionAsync(MarketplaceTransaction transaction);
    }
}
