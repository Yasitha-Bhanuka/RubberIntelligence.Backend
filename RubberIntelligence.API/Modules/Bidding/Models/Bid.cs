using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace RubberIntelligence.API.Modules.Bidding.Models
{
    public class Bid
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string AuctionId { get; set; } = string.Empty;

        public string BidderId { get; set; } = string.Empty;
        public string BidderName { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
