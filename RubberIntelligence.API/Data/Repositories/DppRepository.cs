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
        private readonly IMongoCollection<AccessRequest> _accessRequestCollection;

        public DppRepository(IOptions<MongoDbSettings> settings)
        {
            var mongoClient = new MongoClient(settings.Value.ConnectionString);
            var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
            _collection              = mongoDatabase.GetCollection<DppDocument>("DppDocuments");
            _fieldsCollection        = mongoDatabase.GetCollection<ExtractedField>("ExtractedFields");
            _dppCollection           = mongoDatabase.GetCollection<DigitalProductPassport>("DigitalProductPassports");
            _accessRequestCollection = mongoDatabase.GetCollection<AccessRequest>("AccessRequests");
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

        // ── Controlled Access: AccessRequest ────────────────────────────
        public async Task CreateAccessRequestAsync(AccessRequest request)
            => await _accessRequestCollection.InsertOneAsync(request);

        public async Task<AccessRequest?> GetAccessRequestAsync(string requestId)
            => await _accessRequestCollection.Find(x => x.Id == requestId).FirstOrDefaultAsync();

        public async Task ApproveAccessRequestAsync(string requestId)
        {
            var update = Builders<AccessRequest>.Update
                .Set(x => x.Status,     AccessRequestStatus.Approved)
                .Set(x => x.ApprovedAt, DateTime.UtcNow);
            await _accessRequestCollection.UpdateOneAsync(x => x.Id == requestId, update);
        }

        public async Task RejectAccessRequestAsync(string requestId)
        {
            var update = Builders<AccessRequest>.Update
                .Set(x => x.Status, AccessRequestStatus.Rejected);
            await _accessRequestCollection.UpdateOneAsync(x => x.Id == requestId, update);
        }

        public async Task<AccessRequest?> GetApprovedRequestForLotAndExporterAsync(string lotId, string exporterId)
            => await _accessRequestCollection
                .Find(x => x.LotId == lotId
                         && x.ExporterId == exporterId
                         && x.Status == AccessRequestStatus.Approved)
                .FirstOrDefaultAsync();

        public async Task<List<AccessRequest>> GetPendingRequestsForBuyerAsync(string buyerId)
            => await _accessRequestCollection
                .Find(x => x.BuyerId == buyerId && x.Status == AccessRequestStatus.Pending)
                .SortByDescending(x => x.RequestedAt)
                .ToListAsync();

        public async Task<List<AccessRequest>> GetAccessRequestsByExporterIdAsync(string exporterId)
            => await _accessRequestCollection
                .Find(x => x.ExporterId == exporterId)
                .SortByDescending(x => x.RequestedAt)
                .ToListAsync();
    }
}
