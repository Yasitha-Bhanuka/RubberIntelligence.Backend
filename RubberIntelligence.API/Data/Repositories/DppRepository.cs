using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Data.Repositories
{
    public class DppRepository : IDppRepository
    {
        private readonly IMongoCollection<DppDocument> _collection;

        public DppRepository(IOptions<MongoDbSettings> settings)
        {
            var mongoClient = new MongoClient(settings.Value.ConnectionString);
            var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
            _collection = mongoDatabase.GetCollection<DppDocument>("DppDocuments");
        }

        public async Task CreateAsync(DppDocument document)
        {
            await _collection.InsertOneAsync(document);
        }

        public async Task<DppDocument?> GetByIdAsync(string id)
        {
            return await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        public async Task<List<DppDocument>> GetByBuyerIdAsync(string buyerId)
        {
            return await _collection.Find(x => x.UploadedBy == buyerId).ToListAsync();
        }
    }
}
