using RubberIntelligence.API.Modules.Bidding.DTOs;
using RubberIntelligence.API.Modules.Bidding.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RubberIntelligence.API.Modules.Bidding.Services
{
    public class BiddingService : IBiddingService
    {
        private readonly IBiddingRepository _biddingRepository;

        public BiddingService(IBiddingRepository biddingRepository)
        {
            _biddingRepository = biddingRepository;
        }

        public async Task<List<AuctionDto>> GetActiveAuctionsAsync()
        {
            var auctions = await _biddingRepository.GetActiveAuctionsAsync();
            var activeAuctions = new List<Auction>();
            
            foreach (var auction in auctions)
            {
                // Auto-fix any misconfigured old dummy data or timezone 
                // mismatches to be strictly 1-hour auctions counting down from now.
                if (auction.EndTime > auction.StartTime.AddHours(1.5))
                {
                    auction.StartTime = DateTime.UtcNow;
                    auction.EndTime = DateTime.UtcNow.AddHours(1);
                    await _biddingRepository.UpdateAuctionAsync(auction);
                }

                if (auction.EndTime < DateTime.UtcNow)
                {
                    auction.Status = "Closed";
                    await _biddingRepository.UpdateAuctionAsync(auction);
                }
                else
                {
                    activeAuctions.Add(auction);
                }
            }
            
            return activeAuctions.Select(MapToDto).ToList();
        }

        public async Task<List<AuctionDto>> GetClosedAuctionsAsync()
        {
            var auctions = await _biddingRepository.GetClosedAuctionsAsync();
            return auctions.Select(MapToDto).ToList();
        }

        public async Task<AuctionDto?> GetAuctionDetailsAsync(string id)
        {
            var auction = await _biddingRepository.GetAuctionByIdAsync(id);
            if (auction == null) return null;
            
            if (auction.Status == "Active" && auction.EndTime < DateTime.UtcNow)
            {
                auction.Status = "Closed";
                await _biddingRepository.UpdateAuctionAsync(auction);
            }
            
            return MapToDto(auction);
        }

        public async Task<Auction> CreateAuctionAsync(CreateAuctionDto createAuctionDto, string sellerId, string sellerName)
        {
            var auction = new Auction
            {
                Title = createAuctionDto.Title,
                Subtitle = createAuctionDto.Subtitle,
                Grade = createAuctionDto.Grade,
                CurrentPrice = createAuctionDto.StartingPrice,
                MinIncrement = createAuctionDto.MinIncrement,
                QuantityKg = createAuctionDto.QuantityKg,
                SellerId = sellerId,
                SellerName = sellerName,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddHours(1), // Enforce strictly 1 hour in backend
                Status = "Active",
                LotId = createAuctionDto.LotId,
                IsNftSecured = true // Placeholder for integration
            };

            await _biddingRepository.CreateAuctionAsync(auction);
            return auction;
        }

        public async Task<bool> PlaceBidAsync(string auctionId, CreateBidDto bidDto, string bidderId, string bidderName, string userRole)
        {
            if (userRole.Equals("Farmer", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Farmers are not allowed to place bids.");
            }

            var auction = await _biddingRepository.GetAuctionByIdAsync(auctionId);
            
            if (auction == null || auction.Status != "Active" || auction.EndTime < DateTime.UtcNow)
            {
                return false; // Auction invalid or closed
            }

            // Check if user is the seller
            if (auction.SellerId == bidderId)
            {
                throw new InvalidOperationException("Seller cannot bid on their own auction.");
            }

            // Verify bid amount
            if (bidDto.Amount < auction.CurrentPrice + auction.MinIncrement)
            {
                throw new InvalidOperationException($"Bid amount must be at least {auction.CurrentPrice + auction.MinIncrement}");
            }

            // Create Bid
            var bid = new Bid
            {
                AuctionId = auctionId,
                BidderId = bidderId,
                BidderName = bidderName,
                Amount = bidDto.Amount,
                Timestamp = DateTime.UtcNow
            };

            await _biddingRepository.CreateBidAsync(bid);

            // Update Auction
            auction.CurrentPrice = bidDto.Amount;
            auction.HighestBidderId = bidderId;
            auction.HighestBidderName = bidderName;
            auction.TotalBids += 1;
            auction.UpdatedAt = DateTime.UtcNow;

            await _biddingRepository.UpdateAuctionAsync(auction);

            return true;
        }

        private AuctionDto MapToDto(Auction auction)
        {
            var timeRemainingSpan = auction.EndTime - DateTime.UtcNow;
            var timeRemainingStr = timeRemainingSpan.TotalSeconds > 0 
                ? $"{(int)timeRemainingSpan.TotalDays}d {(int)timeRemainingSpan.Hours}h {timeRemainingSpan.Minutes}m" 
                : "Ended";
                
            var totalDuration = auction.EndTime - auction.StartTime;
            var elapsed = DateTime.UtcNow - auction.StartTime;
            var progress = totalDuration.TotalSeconds > 0 ? elapsed.TotalSeconds / totalDuration.TotalSeconds : 1;

            if (progress < 0) progress = 0;
            if (progress > 1) progress = 1;

            return new AuctionDto
            {
                Id = auction.Id ?? string.Empty,
                Title = auction.Title,
                Subtitle = auction.Subtitle,
                Grade = auction.Grade,
                CurrentPrice = auction.CurrentPrice,
                MinIncrement = auction.MinIncrement,
                Quantity = $"{auction.QuantityKg:N0} kg",
                Seller = auction.SellerName,
                HighestBidder = auction.HighestBidderName ?? "No bids yet",
                TotalBids = auction.TotalBids,
                TimeRemaining = timeRemainingStr.Replace("0d ", ""), // quick format cleanup
                EndTime = auction.EndTime,
                Progress = progress,
                Status = auction.Status,
                IsNftSecured = auction.IsNftSecured,
                NftTokenId = auction.NftTokenId,
                LotId = auction.LotId
            };
        }
    }
}
