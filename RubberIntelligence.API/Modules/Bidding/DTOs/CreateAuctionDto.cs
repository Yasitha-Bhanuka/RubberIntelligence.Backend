using System;

namespace RubberIntelligence.API.Modules.Bidding.DTOs
{
    public class CreateAuctionDto
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public decimal StartingPrice { get; set; }
        public decimal MinIncrement { get; set; }
        public int QuantityKg { get; set; }
        public DateTime EndTime { get; set; }
        public string? LotId { get; set; }
    }
}
