using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Data.Repositories
{
    public class DppRepository : IDppRepository
    {
        private readonly IMongoCollection<DppDocument> _collection;
        private readonly IMongoCollection<ExtractedField> _fieldsCollection;
        private readonly IMongoCollection<DigitalProductPassport> _dppCollection;

        public DppRepository(IOptions<MongoDbSettings> settings)
        {
            var mongoClient = new MongoClient(settings.Value.ConnectionString);
            var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
            _collection     = mongoDatabase.GetCollection<DppDocument>("DppDocuments");
            _fieldsCollection = mongoDatabase.GetCollection<ExtractedField>("ExtractedFields");
            _dppCollection  = mongoDatabase.GetCollection<DigitalProductPassport>("DigitalProductPassports");
        }

        public async Task CreateAsync(DppDocument document)
            => await _collection.InsertOneAsync(document);

        public async Task<DppDocument?> GetByIdAsync(string id)
            => await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();

        public async Task<List<DppDocument>> GetByBuyerIdAsync(string buyerId)
            => await _collection.Find(x => x.UploadedBy == buyerId).ToListAsync();

        public async Task SaveExtractedFieldsAsync(List<ExtractedField> fields)
        {
            if (fields.Count == 0) return;
            await _fieldsCollection.InsertManyAsync(fields);
        }

        // ── Digital Product Passport ──
        public async Task CreateDppAsync(DigitalProductPassport dpp)
            => await _dppCollection.InsertOneAsync(dpp);

        public async Task<DigitalProductPassport?> GetDppByLotIdAsync(string lotId)
            => await _dppCollection.Find(x => x.LotId == lotId).FirstOrDefaultAsync();

        public async Task<List<ExtractedField>> GetExtractedFieldsByLotIdAsync(string lotId)
            => await _fieldsCollection.Find(x => x.LotId == lotId).ToListAsync();
    }
}

