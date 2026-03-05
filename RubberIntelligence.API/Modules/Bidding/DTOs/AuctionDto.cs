using System;

namespace RubberIntelligence.API.Modules.Bidding.DTOs
{
    public class AuctionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        
        public decimal CurrentPrice { get; set; }
        public decimal MinIncrement { get; set; }
        public string Quantity { get; set; } = string.Empty; // E.g. "2,500 kg"
        
        public string Seller { get; set; } = string.Empty;
        public string HighestBidder { get; set; } = string.Empty;
        
        public int TotalBids { get; set; }
        
        public string TimeRemaining { get; set; } = string.Empty; // Calculated like "23m 18s"
        public DateTime EndTime { get; set; }
        public double Progress { get; set; } // 0.0 to 1.0 representing time elapsed
        
        public string Status { get; set; } = string.Empty;
        
        public bool IsNftSecured { get; set; }
        public string? NftTokenId { get; set; }
        public string? IpfsHash { get; set; }
        public int? EsgScore { get; set; }
        public string? LotId { get; set; }
    }
}
