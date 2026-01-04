using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Data.Repositories
{
    public interface IDppRepository
    {
        Task CreateAsync(DppDocument document);
        Task<DppDocument?> GetByIdAsync(string id);
        Task<List<DppDocument>> GetByBuyerIdAsync(string buyerId);
    }
}
