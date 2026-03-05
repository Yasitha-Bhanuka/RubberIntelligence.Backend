using System.Threading.Tasks;

namespace RubberIntelligence.API.Modules.Bidding.Services
{
    public interface IBlockchainService
    {
        Task<string> UploadToIpfsAsync(object metadata);
        Task<string> MintNftAsync(string ipfsHash, string farmerAddress, int esgScore);
        Task<bool> TransferNftAsync(string tokenId, string fromAddress, string toAddress);
    }
}
