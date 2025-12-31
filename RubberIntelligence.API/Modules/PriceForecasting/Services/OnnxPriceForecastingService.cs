using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using RubberIntelligence.API.Modules.PriceForecasting.DTOs;

namespace RubberIntelligence.API.Modules.PriceForecasting.Services
{
    public class OnnxPriceForecastingService : IPriceForecastingService
    {
        private readonly string _modelPath;
        private readonly InferenceSession _session;
        private readonly ILogger<OnnxPriceForecastingService> _logger;

        public OnnxPriceForecastingService(IWebHostEnvironment env, ILogger<OnnxPriceForecastingService> logger)
        {
            _logger = logger;
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
            var moistureTensor = new DenseTensor<float>(new[] { request.MoistureContentPct }, inputDimensions);
            var dirtTensor = new DenseTensor<float>(new[] { request.DirtContentPct }, inputDimensions);
            var qualityTensor = new DenseTensor<float>(new[] { request.VisualQualityScore }, inputDimensions);
            
            var gradeTensor = new DenseTensor<string>(new[] { request.RubberSheetGrade }, inputDimensions);
            var districtTensor = new DenseTensor<string>(new[] { request.District }, inputDimensions);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("quantity_kg", quantityTensor),
                NamedOnnxValue.CreateFromTensor("moisture_content_pct", moistureTensor),
                NamedOnnxValue.CreateFromTensor("dirt_content_pct", dirtTensor),
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

            // 1. Moisture Penalty: Reduce 0.5% per 1% of moisture
            if (request.MoistureContentPct > 0)
            {
                float moisturePenalty = request.MoistureContentPct * 0.005f; 
                predictedPrice *= (1.0f - moisturePenalty);
                _logger.LogInformation($"[PriceAI] Applied Moisture Penalty: -{moisturePenalty:P1}");
            }

            // 2. Dirt Penalty: Reduce 1% per 1% of dirt
            if (request.DirtContentPct > 0)
            {
                float dirtPenalty = request.DirtContentPct * 0.01f;
                predictedPrice *= (1.0f - dirtPenalty);
                _logger.LogInformation($"[PriceAI] Applied Dirt Penalty: -{dirtPenalty:P1}");
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
                 // "Immediately" gets no penalty (or could get a bonus)
            }

            // Ensure price doesn't go negative
            if (predictedPrice < 0) predictedPrice = 0;

            _logger.LogInformation($"[PriceAI] Final Adjusted Price: {predictedPrice}");

            return new PricePredictionResponse
            {
                PredictedPriceLkr = predictedPrice,
                Currency = "LKR"
            };
        }

        public async Task<IEnumerable<PriceHistoryItem>> GetPriceHistoryAsync()
        {
            // Mock data for now - in a real app this would come from a database
            var history = new List<PriceHistoryItem>();
            var today = DateTime.Today;
            var random = new Random();

            for (int i = 30; i >= 0; i--)
            {
                history.Add(new PriceHistoryItem
                {
                    Date = today.AddDays(-i),
                    Price = 500 + (random.NextDouble() * 100 - 50), // Random fluctuation around 500
                    Grade = "RSS1"
                });
            }

            return await Task.FromResult(history);
        }
    }
}
