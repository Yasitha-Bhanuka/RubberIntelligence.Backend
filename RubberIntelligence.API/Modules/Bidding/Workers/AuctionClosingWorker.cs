using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RubberIntelligence.API.Modules.Bidding.Hubs;
using RubberIntelligence.API.Modules.Bidding.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RubberIntelligence.API.Modules.Bidding.Workers
{
    public class AuctionClosingWorker : BackgroundService
    {
        private readonly ILogger<AuctionClosingWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<AuctionHub> _hubContext;

        public AuctionClosingWorker(
            ILogger<AuctionClosingWorker> logger,
            IServiceProvider serviceProvider,
            IHubContext<AuctionHub> hubContext)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var biddingRepository = scope.ServiceProvider.GetRequiredService<IBiddingRepository>();
                        var blockchainService = scope.ServiceProvider.GetRequiredService<IBlockchainService>();

                        var activeAuctions = await biddingRepository.GetActiveAuctionsAsync();

                        foreach (var auction in activeAuctions)
                        {
                            if (auction.EndTime <= DateTime.UtcNow)
                            {
                                _logger.LogInformation($"Auction {auction.Id} has ended. Closing and transferring NFT.");

                                auction.Status = "Closed";

                                // Transfer NFT if secured and there's a winner
                                if (auction.IsNftSecured && !string.IsNullOrEmpty(auction.NftTokenId) && !string.IsNullOrEmpty(auction.HighestBidderId))
                                {
                                    // Normally we would use real wallet addresses, using IDs as mocks here
                                    await blockchainService.TransferNftAsync(auction.NftTokenId, auction.SellerId, auction.HighestBidderId);
                                    _logger.LogInformation($"Transferred NFT {auction.NftTokenId} to {auction.HighestBidderName}");
                                }

                                await biddingRepository.UpdateAuctionAsync(auction);

                                // Broadcast closure to all clients in the auction group
                                await _hubContext.Clients.Group(auction.Id).SendAsync("AuctionClosed", auction.Id, auction.HighestBidderName, auction.CurrentPrice);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing AuctionClosingWorker.");
                }

                // Check every 30 seconds
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
