using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Data.Repositories
{
    public interface IDppRepository
    {
        Task CreateAsync(DppDocument document);
        Task<DppDocument?> GetByIdAsync(string id);
        Task<List<DppDocument>> GetByBuyerIdAsync(string buyerId);
        Task SaveExtractedFieldsAsync(List<ExtractedField> fields);

        // Digital Product Passport
        Task CreateDppAsync(DigitalProductPassport dpp);
        Task<DigitalProductPassport?> GetDppByLotIdAsync(string lotId);
        Task<List<ExtractedField>> GetExtractedFieldsByLotIdAsync(string lotId);
    }
}
