# 🔗 Blockchain-Based Bidding Platform

## 📋 Overview
A secure, transparent, and real-time digital auction system for the rubber industry. This component ensures that every transaction is traceable and every bid is broadcasted instantly to all participants.

## 🚀 Key Features
- **Real-Time Bidding:** Powered by **SignalR (WebSockets)** for instant price updates without page refreshes.
- **NFT Tokenization:** Every auction lot is treated as a unique NFT, with metadata stored on **IPFS** for immutable record-keeping.
- **Automated Closing:** A background worker monitors auctions every 30 seconds and automatically settles them upon expiration.
- **Traceability:** Integrated with the Digital Product Passport (DPP) system to track the origin of every rubber lot.

## 🛠️ Technology Stack
- **Backend:** ASP.NET Core 8.0.
- **Real-time:** SignalR.
- **Storage:** MongoDB.
- **Blockchain Simulation:** Mock service for NFT minting and IPFS hashing.

## 🏛️ Auction Lifecycle
1.  **Creation:** Farmer creates a lot; the system mints an NFT and uploads metadata to IPFS.
2.  **Bidding:** Buyers place bids; SignalR broadcasts the new "Highest Bid" to everyone.
3.  **Validation:** The system enforces rules (e.g., Sellers cannot bid on their own items).
4.  **Settlement:** When time runs out, the background worker closes the auction and "transfers" the NFT to the winner.

## 🛡️ Business Rules
- **Farmers** cannot place bids.
- **Sellers** cannot bid on their own auctions.
- Bids must exceed the current price by a **Minimum Increment**.
- Auctions cannot be modified once they are closed.

## 📁 File Structure
- `Hubs/`: SignalR WebSocket hub for real-time communication.
- `Workers/`: Background service for closing expired auctions.
- `Services/`: Core business logic and blockchain simulation.
- `Controllers/`: REST endpoints for auction management.
