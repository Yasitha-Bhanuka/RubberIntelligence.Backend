using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RubberIntelligence.API.Modules.Dpp.Models
{
    public class DppDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFilePath { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;

        public string Classification { get; set; } = string.Empty;
        public double ConfidenceScore { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public string UploadedBy { get; set; } = string.Empty;

        public string? ExtractedTextSummary { get; set; }
        public List<string> DetectedKeywords { get; set; } = new();
    }
}
