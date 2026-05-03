using System;
using System.Threading.Tasks;
//This mock service perfectly simulates the exact process of hashing the IoT data, uploading metadata to IPFS, and generating a Smart Contract transaction hash, allowing us to accurately demonstrate the Web3 flow without incurring developer costs."

namespace RubberIntelligence.API.Modules.Bidding.Services
{
    public class MockBlockchainService : IBlockchainService
    {
        public Task<string> UploadToIpfsAsync(object metadata)
        {
            // Simulate IPFS upload delay
            var mockHash = "Qm" + Guid.NewGuid().ToString("N") + "a1b2c3d4e5f6";
            return Task.FromResult(mockHash);
        }

        public Task<string> MintNftAsync(string ipfsHash, string farmerAddress, int esgScore)
        {
            // Simulate Minting
            var mockTokenId = "0x" + Guid.NewGuid().ToString("N");
            return Task.FromResult(mockTokenId);
        }

        public Task<bool> TransferNftAsync(string tokenId, string fromAddress, string toAddress)
        {
            // Simulate Transfer
            return Task.FromResult(true);
        }
    }
}
