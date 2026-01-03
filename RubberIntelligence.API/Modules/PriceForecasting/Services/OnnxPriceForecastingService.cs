using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using RubberIntelligence.API.Modules.PriceForecasting.DTOs;
using RubberIntelligence.API.Data; // Added
using RubberIntelligence.API.Domain.Entities; // Added
using MongoDB.Driver; // Added

namespace RubberIntelligence.API.Modules.PriceForecasting.Services
{
    public class OnnxPriceForecastingService : IPriceForecastingService
    {
        private readonly string _modelPath;
        private readonly InferenceSession _session;
        private readonly ILogger<OnnxPriceForecastingService> _logger;
        private readonly AppDbContext _context; // Added

        public OnnxPriceForecastingService(IWebHostEnvironment env, ILogger<OnnxPriceForecastingService> logger, AppDbContext context)
        {
            _logger = logger;
            _context = context; // Added
            _modelPath = Path.Combine(env.ContentRootPath, "Modules", "PriceForecasting", "Models", "rubber_price_model.onnx");

            if (File.Exists(_modelPath))
            {
                try
                {
                    _session = new InferenceSession(_modelPath);
                    _logger.LogInformation($"[PriceAI] ONNX Model loaded from {_modelPath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PriceAI] Failed to load ONNX model");
                }
            }
            else
            {
                _logger.LogWarning($"[PriceAI] Model file not found at {_modelPath}");
            }
        }

        public async Task<PricePredictionResponse> PredictPriceAsync(PricePredictionRequest request)
        {
            if (_session == null)
            {
                throw new InvalidOperationException("Price Forecasting Model is not available.");
            }

            _logger.LogInformation($"[PriceAI] Predicting for Grade={request.RubberSheetGrade}, Qty={request.QuantityKg}");

            // 1. Prepare Inputs
            // The model expects [1, 1] tensors for each feature
            var inputDimensions = new int[] { 1, 1 };

            var quantityTensor = new DenseTensor<float>(new[] { request.QuantityKg }, inputDimensions);
            // UPDATED: Use string tensors for new categorical inputs
            var moistureTensor = new DenseTensor<string>(new[] { request.MoistureLevel }, inputDimensions);
            var dirtTensor = new DenseTensor<string>(new[] { request.Cleanliness }, inputDimensions);
            var qualityTensor = new DenseTensor<float>(new[] { request.VisualQualityScore }, inputDimensions);
            
            var gradeTensor = new DenseTensor<string>(new[] { request.RubberSheetGrade }, inputDimensions);
            var districtTensor = new DenseTensor<string>(new[] { request.District }, inputDimensions);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("quantity_kg", quantityTensor),
                NamedOnnxValue.CreateFromTensor("Moisture_Level", moistureTensor), // Case sensitive name from Python script
                NamedOnnxValue.CreateFromTensor("Cleanliness", dirtTensor),       // Case sensitive name from Python script
                NamedOnnxValue.CreateFromTensor("visual_quality_score", qualityTensor),
                NamedOnnxValue.CreateFromTensor("rubber_sheet_grade", gradeTensor),
                NamedOnnxValue.CreateFromTensor("district", districtTensor)
            };

            // 2. Run Inference
            using var results = _session.Run(inputs);

            // 3. Extract Output
            // The output name for sklearn variable is usually "variable" or "output_label"
            // For Regressor, it is often "variable" (float tensor)
            var outputTensor = results.First().AsTensor<float>();
            float predictedPrice = outputTensor[0];

            _logger.LogInformation($"[PriceAI] Base Model Price: {predictedPrice}");

            // --- Rules-Based Adjustments ---

            // 1. Moisture Penalty
            // Adapted logic: Fixed penalty for 'Wet'
            if (string.Equals(request.MoistureLevel, "Wet", StringComparison.OrdinalIgnoreCase))
            {
                // Approx penalty for >3% moisture
                float moisturePenalty = 0.05f; // 5% penalty
                predictedPrice *= (1.0f - moisturePenalty);
                _logger.LogInformation($"[PriceAI] Applied Moisture Penalty (Wet): -{moisturePenalty:P1}");
            }

            // 2. Dirt Penalty
            // Adapted logic: Fixed penalty for 'Dirty', smaller for 'Slight'
            if (string.Equals(request.Cleanliness, "Dirty", StringComparison.OrdinalIgnoreCase))
            {
                float dirtPenalty = 0.05f; // 5% penalty
                predictedPrice *= (1.0f - dirtPenalty);
                _logger.LogInformation($"[PriceAI] Applied Dirt Penalty (Dirty): -{dirtPenalty:P1}");
            }
            else if (string.Equals(request.Cleanliness, "Slight", StringComparison.OrdinalIgnoreCase))
            {
                float dirtPenalty = 0.02f; // 2% penalty
                predictedPrice *= (1.0f - dirtPenalty);
                _logger.LogInformation($"[PriceAI] Applied Dirt Penalty (Slight): -{dirtPenalty:P1}");
            }

            // 3. Market Availability Adjustment
            if (!string.IsNullOrEmpty(request.MarketAvailability))
            {
                if (request.MarketAvailability.Contains("1 week", StringComparison.OrdinalIgnoreCase))
                {
                    predictedPrice *= 0.98f; // -2%
                    _logger.LogInformation("[PriceAI] Applied Availability Penalty: -2% (1 week)");
                }
                else if (request.MarketAvailability.Contains("2 weeks", StringComparison.OrdinalIgnoreCase))
                {
                    predictedPrice *= 0.95f; // -5%
                    _logger.LogInformation("[PriceAI] Applied Availability Penalty: -5% (2 weeks)");
                }
            }

            // Ensure price doesn't go negative
            if (predictedPrice < 0) predictedPrice = 0;

            _logger.LogInformation($"[PriceAI] Final Adjusted Price: {predictedPrice}");

            // === SAVE TO DB ===
            try
            {
                var record = new PredictionRecord
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    RubberSheetGrade = request.RubberSheetGrade,
                    QuantityKg = request.QuantityKg,
                    MoistureLevel = request.MoistureLevel,
                    Cleanliness = request.Cleanliness,
                    VisualQualityScore = request.VisualQualityScore,
                    District = request.District,
                    MarketAvailability = request.MarketAvailability,
                    PredictedPriceLkr = predictedPrice
                };
                await _context.PredictionRecords.InsertOneAsync(record);
                _logger.LogInformation("[PriceAI] Saved prediction to database.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PriceAI] Failed to save prediction to DB.");
                // Don't fail the request if saving fails, just log it
            }

            return new PricePredictionResponse
            {
                PredictedPriceLkr = predictedPrice,
                Currency = "LKR"
            };
        }

        public async Task<IEnumerable<PriceHistoryItem>> GetPriceHistoryAsync()
        {
            try
            {
                // Fetch from DB
                var records = await _context.PredictionRecords
                    .Find(_ => true)
                    .SortByDescending(x => x.Timestamp)
                    .Limit(50) // Limit to last 50 items
                    .ToListAsync();
                
                return records.Select(r => new PriceHistoryItem
                {
                    Date = r.Timestamp.Date, // Use local time conversion in frontend or here if needed
                    Price = (double)r.PredictedPriceLkr,
                    Grade = r.RubberSheetGrade ?? "Unknown"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PriceAI] Failed to fetch history.");
                return new List<PriceHistoryItem>();
            }
        }
    }
}
