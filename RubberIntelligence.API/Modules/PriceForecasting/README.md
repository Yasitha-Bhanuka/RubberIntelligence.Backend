# 🤖 AI-Driven Auction Price Forecasting

## 📋 Overview
This component leverages Machine Learning to predict the market price of rubber lots in Sri Lanka. It helps farmers understand the value of their produce before participating in auctions, ensuring fair trade and transparency.

## 🚀 Key Features
- **Accurate Predictions:** Uses a Random Forest Regressor trained on real historical data from RRISL and RDD.
- **Rule-Based Adjustments:** Incorporates industry expertise by adjusting prices based on moisture levels, cleanliness, and market availability.
- **Fast Inference:** Deployed using **ONNX**, allowing Python-trained models to run directly within the C# ASP.NET Core environment.
- **Historical Tracking:** Saves all predictions to MongoDB for future reference and trend analysis.

## 🛠️ Technology Stack
- **Training:** Python, scikit-learn, Pandas.
- **Deployment:** ONNX Runtime (C#).
- **Backend:** ASP.NET Core 8.0.
- **Database:** MongoDB.

## 🧠 Model Logic
1.  **Preprocessing:** Categorical data (Grade, District) is One-Hot Encoded, and numerical data is scaled.
2.  **Algorithm:** Random Forest Regressor (100 estimators).
3.  **Adjustments:**
    - `MoistureLevel == "Wet"`: **-5%** price penalty.
    - `Cleanliness == "Dirty"`: **-5%** price penalty.
    - `MarketAvailability == "2 weeks"`: **-5%** price penalty.

## 🔌 API Endpoints
- `POST /api/price/forecast`: Predicts the price for a new rubber lot.
- `GET /api/price/history`: Retrieves the last 50 prediction records.

## 📁 File Structure
- `Controllers/`: API endpoints.
- `Models/`: Contains the `.onnx` model file.
- `Services/`: Logic for loading the model and running inference.
- `DTOs/`: Data Transfer Objects for requests and responses.
