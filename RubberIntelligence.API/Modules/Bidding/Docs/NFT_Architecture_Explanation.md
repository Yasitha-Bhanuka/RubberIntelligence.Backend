# Digital Passport (NFT) Architecture Explanation

This document clarifies how the Digital Passport (NFT) functions within the Rubber Intelligence Platform, specifically focusing on its role in the Bidding System.

## What is the Digital Passport (NFT)?

In the context of the Rubber Intelligence App, a Digital Passport is a Non-Fungible Token (NFT) minted on a blockchain network. It represents cryptographic proof of existence, quality, and ownership of a specific physical lot of rubber. 

Instead of relying solely on a centralized database, the NFT provides an immutable, verifiable public record that guarantees the authenticity of the rubber lot's details (grade, origin, quantity).

## The Workflow

### 1. Creation & Minting (Farmer/System)
- A **Farmer** adds a new rubber lot to the system after taking photos and getting the quality/grade predicted.
- The **Backend (DPP Module)** takes the verified details (Lot ID, Origin, Grade, Quantity, Farmer ID) and interacts with a Smart Contract on the Blockchain to "mint" an NFT.
- A unique `TokenId` (e.g., `0x7b...82a`) is generated and assigned to the Farmer's digital wallet address.
- The relational database (MongoDB) stores this `TokenId` alongside the standard `Auction` and `Lot` records.

### 2. Auction Phase (Bidding Platform)
- When the Farmer lists this lot on the **Bidding Platform**, the frontend (`AuctionBiddingScreen`) requests the auction details from the backend.
- The `AuctionDto` returns `IsNftSecured: true` and the `NftTokenId`.
- The Frontend displays the "NFT Secured" badge and shows the Token ID. This builds immense trust for Buyers/Exporters, as they know exactly what they are bidding on can be cryptographically verified.
- The **Traceability** screen uses this `LotId` and `NftTokenId` to pull the full history of the rubber block from the blockchain.

### 3. Post-Auction Ownership Transfer (Settlement)
- When an auction concludes and payment is settled, a transaction occurs on the blockchain.
- The Backend triggers a Smart Contract call to *transfer* the NFT from the Farmer's wallet address to the winning Buyer/Exporter's wallet address.
- The Buyer now holds the cryptographic proof of ownership for that specific premium rubber lot, which is vital for international shipping compliance and proving fair trade sourcing to end consumers.

## System Connections

- **Frontend (`rubber-intelligence-app`)**: Briefly displays the `NftTokenId` and links to the Traceability module via route parameters. It primarily consumes the boolean flag `IsNftSecured` to render UI trust badges.
- **Backend API (`RubberIntelligence.API`)**: Stores the `NftTokenId` as a string in MongoDB. It acts as the bridge between the UI and the Blockchain node.
- **Blockchain Storage**: The actual decentralized ledger where the Smart Contract lives. Only essential metadata (ID, basic hashes) is typically stored on-chain to save gas fees, while large data (images) remains off-chain, linked via the NFT's metadata URI.
