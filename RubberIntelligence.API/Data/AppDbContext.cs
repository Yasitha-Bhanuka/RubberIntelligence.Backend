using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RubberIntelligence.API.Domain.Entities;

namespace RubberIntelligence.API.Data
{
    public class AppDbContext
    {
        private readonly IMongoDatabase _database;

        public AppDbContext(IOptions<MongoDbSettings> mongoSettings)
        {
            var client = new MongoClient(mongoSettings.Value.ConnectionString);
            _database = client.GetDatabase(mongoSettings.Value.DatabaseName);
        }

        public IMongoCollection<User> Users => _database.GetCollection<User>("Users");
        public IMongoCollection<DiseaseRecord> DiseaseRecords => _database.GetCollection<DiseaseRecord>("DiseaseRecords");
    }
}
