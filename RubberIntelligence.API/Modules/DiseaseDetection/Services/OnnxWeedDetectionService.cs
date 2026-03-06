using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    /// <summary>
    /// Uses the rubber leaf disease ONNX model to classify weed images.
    /// Since weeds are plants, the leaf disease model can identify key conditions
    /// (healthy, diseased, pest-affected) on weed/plant images.
    /// The output labels are mapped to weed-specific terminology.
    /// </summary>
    public class OnnxWeedDetectionService : IDiseaseDetectionService
    {
        private readonly InferenceSession? _session;
        private readonly ILogger<OnnxWeedDetectionService> _logger;

        // Same classes as the leaf disease model — we reinterpret them for weeds
        private readonly string[] _modelLabels = {
            "Anthracnose",
            "Birds_eye",
            "Colletorichum",
            "Corynespora",
            "Dry_Leaf",
            "Healthy",
            "Leaf_Spot",
            "Pesta",
            "Powdery_mildew"
        };

        public OnnxWeedDetectionService(IWebHostEnvironment env, ILogger<OnnxWeedDetectionService> logger)
        {
            _logger = logger;
            var modelPath = Path.Combine(env.ContentRootPath, "Modules", "DiseaseDetection", "Models", "rubber_leaf_disease_model.onnx");

            if (File.Exists(modelPath))
            {
                try
                {
                    _session = new InferenceSession(modelPath);
                    _logger.LogInformation("[AI-Weed] ONNX Model loaded from {Path}", modelPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AI-Weed] Failed to load ONNX model at {Path}", modelPath);
                }
            }
            else
            {
                _logger.LogError("[AI-Weed] ONNX Model NOT FOUND at {Path}", modelPath);
            }
        }

        public async Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
            _logger.LogInformation("[AI-Weed] Starting ONNX Inference for Weed Detection...");

            if (_session == null)
            {
                return new PredictionResponse
                {
                    Label = "Model Unavailable",
                    Confidence = 0,
                    Severity = "N/A",
                    Remedy = "The weed detection model is not loaded. Please check server logs."
                };
            }

            // 1. Preprocess Image (same as leaf disease — 224x224, ImageNet normalization)
            using var stream = request.Image.OpenReadStream();
            using var image = await Image.LoadAsync<Rgb24>(stream);
            image.Mutate(x => x.Resize(224, 224));

            var denseTensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 });
            float[] mean = { 0.485f, 0.456f, 0.406f };
            float[] std = { 0.229f, 0.224f, 0.225f };

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++)
                    {
                        var pixel = pixelRow[x];
                        denseTensor[0, 0, y, x] = ((pixel.R / 255f) - mean[0]) / std[0];
                        denseTensor[0, 1, y, x] = ((pixel.G / 255f) - mean[1]) / std[1];
                        denseTensor[0, 2, y, x] = ((pixel.B / 255f) - mean[2]) / std[2];
                    }
                }
            });

            // 2. Run Inference
            var inputName = _session.InputMetadata.Keys.First();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, denseTensor)
            };

            using var results = _session.Run(inputs);
            var outputLogits = results.First().AsTensor<float>().ToArray();

            // 3. Softmax
            var probabilities = Softmax(outputLogits);

            // 4. Find max
            int maxIndex = 0;
            float maxScore = -1;
            for (int i = 0; i < probabilities.Length; i++)
            {
                if (probabilities[i] > maxScore)
                {
                    maxScore = probabilities[i];
                    maxIndex = i;
                }
            }

            // 5. Map model label to weed-specific output
            string modelLabel = _modelLabels[maxIndex];
            var (weedLabel, remedy) = MapToWeedResult(modelLabel);

            string severity = maxScore switch
            {
                > 0.8f => "High",
                > 0.5f => "Medium",
                _ => "Low"
            };

            _logger.LogInformation("[AI-Weed] Prediction: {Label} (model: {ModelLabel}, confidence: {Confidence:P2})",
                weedLabel, modelLabel, maxScore);

            return new PredictionResponse
            {
                Label = weedLabel,
                Confidence = maxScore,
                Severity = severity,
                Remedy = remedy
            };
        }

        /// <summary>
        /// Maps leaf disease model labels to weed-relevant descriptions.
        /// </summary>
        private static (string Label, string Remedy) MapToWeedResult(string modelLabel)
        {
            return modelLabel switch
            {
                "Healthy" => (
                    "Healthy Weed",
                    "This plant appears healthy. If it is an unwanted weed, apply appropriate herbicide (Glyphosate or Paraquat) or remove by manual weeding."
                ),
                "Anthracnose" => (
                    "Weed with Fungal Infection",
                    "This weed shows signs of anthracnose fungal infection. The disease may spread to nearby rubber trees. Remove the weed and apply copper-based fungicide in the surrounding area."
                ),
                "Powdery_mildew" => (
                    "Weed with Powdery Mildew",
                    "Powdery mildew detected on this plant. Remove the weed to prevent spread. Apply sulfur-based treatment if surrounding rubber trees show similar symptoms."
                ),
                "Leaf_Spot" or "Corynespora" or "Colletorichum" => (
                    "Weed with Leaf Disease",
                    "Leaf disease detected on this plant. Remove the weed and fallen leaves. Monitor nearby rubber trees for similar symptoms."
                ),
                "Birds_eye" => (
                    "Weed with Bacterial Infection",
                    "Signs of bacterial infection on this plant. Remove and destroy the weed. Avoid overhead irrigation in the area."
                ),
                "Dry_Leaf" => (
                    "Dried/Stressed Weed",
                    "This plant shows drought stress or drying. If it is a weed, it may be naturally dying. No herbicide needed — manual removal is sufficient."
                ),
                "Pesta" => (
                    "Pest-Affected Weed",
                    "This weed shows signs of pest infestation. Remove the weed to prevent pests from migrating to rubber trees. Check nearby trees for pest activity."
                ),
                _ => (
                    $"Weed ({modelLabel})",
                    "Weed condition detected. If unwanted, apply appropriate herbicide or manual weeding. Monitor nearby rubber trees."
                )
            };
        }

        private float[] Softmax(float[] logits)
        {
            var maxLogit = logits.Max();
            var exp = logits.Select(x => Math.Exp(x - maxLogit)).ToArray();
            var sum = exp.Sum();
            return exp.Select(x => (float)(x / sum)).ToArray();
        }
    }
}
