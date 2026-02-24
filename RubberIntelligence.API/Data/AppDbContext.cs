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
        public IMongoCollection<PredictionRecord> PredictionRecords => _database.GetCollection<PredictionRecord>("PredictionRecords");
        public IMongoCollection<Alert> Alerts => _database.GetCollection<Alert>("Alerts");

        /// <summary>
        /// Creates 2dsphere geospatial indexes for location-based queries.
        /// Called once at application startup.
        /// </summary>
        public async Task EnsureIndexesAsync()
        {
            // 2dsphere index on User.Location for finding nearby farmers
            await Users.Indexes.CreateOneAsync(
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Geo2DSphere(u => u.Location)));

            // 2dsphere index on DiseaseRecord.Location for spatial disease queries
            await DiseaseRecords.Indexes.CreateOneAsync(
                new CreateIndexModel<DiseaseRecord>(
                    Builders<DiseaseRecord>.IndexKeys.Geo2DSphere(r => r.Location)));
        }
    }
}

