namespace RubberIntelligence.API.Data
{
    public class MongoDbSettings
    {
        public const string SectionName = "MongoDbSettings";
        public required string ConnectionString { get; set; }
        public required string DatabaseName { get; set; }
    }
}
