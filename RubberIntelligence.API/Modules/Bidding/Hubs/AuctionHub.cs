using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace RubberIntelligence.API.Modules.Bidding.Hubs
{
    public class AuctionHub : Hub
    {
        public async Task JoinAuctionGroup(string auctionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, auctionId);
        }

        public async Task LeaveAuctionGroup(string auctionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, auctionId);
        }
    }
}
