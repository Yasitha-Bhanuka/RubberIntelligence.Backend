using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RubberIntelligence.API.Data;
using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Data.Repositories
{
    public class MessageRepository : IMessageRepository
    {
        private readonly IMongoCollection<Message> _collection;

        public MessageRepository(IOptions<MongoDbSettings> settings)
        {
            var client = new MongoClient(settings.Value.ConnectionString);
            var db = client.GetDatabase(settings.Value.DatabaseName);
            _collection = db.GetCollection<Message>("LotMessages");
        }

        public async Task CreateAsync(Message message)
            => await _collection.InsertOneAsync(message);

        public async Task<List<Message>> GetByLotIdAsync(string lotId)
            => await _collection.Find(x => x.LotId == lotId)
                .SortBy(x => x.CreatedAt)
                .ToListAsync();

        public async Task<long> GetUnreadCountAsync(string receiverId)
            => await _collection.CountDocumentsAsync(x => x.ReceiverId == receiverId);
    }
}
