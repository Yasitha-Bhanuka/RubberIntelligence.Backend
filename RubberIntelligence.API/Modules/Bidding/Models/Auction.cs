using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace RubberIntelligence.API.Modules.Bidding.Models
{
    [BsonIgnoreExtraElements]
    public class Auction
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        
        public string? MintTxHash { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty; // e.g., RSS1, RSS3
        
        public decimal CurrentPrice { get; set; }
        public decimal MinIncrement { get; set; }
        
        public int QuantityKg { get; set; }
        
        // References to users
        public string SellerId { get; set; } = string.Empty;
        public string SellerName { get; set; } = string.Empty;

        public string? HighestBidderId { get; set; }
        public string? HighestBidderName { get; set; }

        public int TotalBids { get; set; } = 0;

        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime EndTime { get; set; }

        public string Status { get; set; } = "Active"; // Active, Closed, Cancelled

        public bool IsNftSecured { get; set; } = true;
        public string? NftTokenId { get; set; }
        public string? LotId { get; set; } // Reference to traceability lot
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
