using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class OnnxPestDetectionService : IDiseaseDetectionService
    {
        private readonly string _modelPath;
        private readonly InferenceSession _session;
        // Classes from User's pests_labels.json
        private readonly string[] _labels = {
            "Adristyrannus",
            "Aphids",
            "Beetle",
            "Bugs",
            "Cabbage Looper",
            "Cicadellidae",
            "Cutworm",
            "Earwig",
            "FieldCricket",
            "Grasshopper",
            "Mediterranean fruit fly",
            "Mites",
            "RedSpider",
            "Riptortus",
            "Slug",
            "Snail",
            "Thrips",
            "Weevil",
            "Whitefly"
        };
        private readonly ILogger<OnnxPestDetectionService> _logger;

        public OnnxPestDetectionService(IWebHostEnvironment env, ILogger<OnnxPestDetectionService> logger)
        {
            _logger = logger;
            _modelPath = Path.Combine(env.ContentRootPath, "Modules", "DiseaseDetection", "Models", "pests_model.onnx");
            
            if (File.Exists(_modelPath))
            {
                try 
                {
                    _session = new InferenceSession(_modelPath);
                    _logger.LogInformation($"[AI] ONNX Model loaded successfully from {_modelPath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[AI] Failed to load ONNX model at {_modelPath}");
                }
            }
            else
            {
                _logger.LogError($"[AI] ONNX Model NOT FOUND at {_modelPath}");
            }
        }

        public async Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
            _logger.LogInformation("[AI] Starting ONNX Inference for Pest Detection...");

            if (_session == null)
            {
                // Warn but don't crash if possible, or return a friendly error
                throw new FileNotFoundException($"Pest ONNX Model not found at {_modelPath}. Please upload it.");
            }

            // 1. Preprocess Image (FastAI / ImageNet Standard)
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
                        denseTensor[0, 0, y, x] = ((pixel.R / 255f) - mean[0]) / std[0]; // R
                        denseTensor[0, 1, y, x] = ((pixel.G / 255f) - mean[1]) / std[1]; // G
                        denseTensor[0, 2, y, x] = ((pixel.B / 255f) - mean[2]) / std[2]; // B
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

            // 4. Find Max
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

            // 5. Confidence Threshold Check (Unrecognized Pest)
            // If the model is not confident, this insect is likely not one of the 19 trained pests.
            float confidenceThreshold = 0.55f; // Slightly lower for pests given higher class count

            if (maxScore < confidenceThreshold)
            {
                _logger.LogWarning($"[AI] Low confidence pest ({maxScore:P2}). Rejecting as unrecognized.");
                return new PredictionResponse
                {
                    Label = "Unrecognized Pest",
                    Confidence = maxScore,
                    Severity = "N/A",
                    Remedy = "The model could not confidently match this insect to our known rubber plantation pests. It may be harmless or require expert identification.",
                    IsRejected = true,
                    RejectionReason = $"Low confidence ({maxScore:P2}). Insect does not match known rubber pests."
                };
            }

            // 6. Map to Result
            string predictedLabel = _labels[maxIndex];
            string remedy = GetRemedy(predictedLabel);

            _logger.LogInformation($"[AI] Inference Complete. Prediction: {predictedLabel}, Confidence: {maxScore:P2}");
            
            return new PredictionResponse
            {
                Label = predictedLabel,
                Confidence = maxScore,
                Severity = maxScore > 0.8 ? "High" : "Medium",
                Remedy = remedy
            };
        }

        private float[] Softmax(float[] logits)
        {
            var maxLogit = logits.Max();
            var exp = logits.Select(x => Math.Exp(x - maxLogit)).ToArray();
            var sum = exp.Sum();
            return exp.Select(x => (float)(x / sum)).ToArray();
        }

        private string GetRemedy(string label)
        {
            // Generic remedies based on common pest management
            return label switch
            {
                "Aphids" or "Mites" or "RedSpider" or "Whitefly" or "Thrips" => "Use neem oil or insecticidal soap. Encourage natural predators like ladybugs.",
                "Beetle" or "Weevil" => "Pick off visible beetles. Apply neem oil or pyrethrin-based insecticides if infestation is severe.",
                "Cabbage Looper" or "Cutworm" or "Slug" or "Snail" => "Remove manually. Use diatomaceous earth or organic bait. Check under leaves.",
                "Mediterranean fruit fly" => "Use pheromone traps and certified insecticides. Destroy fallen infected fruit.",
                "Grasshopper" or "FieldCricket" or "Earwig" => "Keep area free of debris. Use neem oil sprays.",
                _ => "Apply general insecticide or consult an agriculture extension officer."
            };
        }
    }
}
