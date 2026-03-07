using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Data.Repositories
{
    public class DocumentAccessGrantRepository : IDocumentAccessGrantRepository
    {
        private readonly IMongoCollection<DocumentAccessGrant> _collection;

        public DocumentAccessGrantRepository(IOptions<MongoDbSettings> settings)
        {
            var mongoClient = new MongoClient(settings.Value.ConnectionString);
            var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
            _collection = mongoDatabase.GetCollection<DocumentAccessGrant>("DocumentAccessGrants");
        }

        public async Task CreateGrantAsync(DocumentAccessGrant grant)
            => await _collection.InsertOneAsync(grant);

        public async Task<DocumentAccessGrant?> GetGrantAsync(string dppDocumentId, string exporterId)
            => await _collection.Find(x => x.DppDocumentId == dppDocumentId && x.ExporterId == exporterId)
                .FirstOrDefaultAsync();

        public async Task<List<DocumentAccessGrant>> GetGrantsByExporterIdAsync(string exporterId)
            => await _collection.Find(x => x.ExporterId == exporterId).ToListAsync();

        public async Task<List<DocumentAccessGrant>> GetGrantsByDocumentIdAsync(string dppDocumentId)
            => await _collection.Find(x => x.DppDocumentId == dppDocumentId).ToListAsync();

        public async Task RevokeGrantAsync(string grantId)
            => await _collection.DeleteOneAsync(x => x.Id == grantId);

        public async Task<bool> HasAccessAsync(string dppDocumentId, string exporterId)
        {
            var grant = await GetGrantAsync(dppDocumentId, exporterId);
            return grant != null;
        }
    }
}
