using System.Threading.Tasks;

//mint an NFT for a rubber lot and upload the environmental data to the decentralized IPFS storage."

namespace RubberIntelligence.API.Modules.Bidding.Services
{
    public interface IBlockchainService
    {
        Task<string> UploadToIpfsAsync(object metadata);
        Task<string> MintNftAsync(string ipfsHash, string farmerAddress, int esgScore);
        Task<bool> TransferNftAsync(string tokenId, string fromAddress, string toAddress);
    }
}
