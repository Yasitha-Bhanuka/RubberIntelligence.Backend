using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class OnnxLeafDiseaseService : IDiseaseDetectionService
    {
        private readonly string _modelPath;
        private readonly InferenceSession _session;
        // Updated Classes from FastAI Model
        private readonly string[] _labels = {
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
        private readonly ILogger<OnnxLeafDiseaseService> _logger;

        public OnnxLeafDiseaseService(IWebHostEnvironment env, ILogger<OnnxLeafDiseaseService> logger)
        {
            _logger = logger;
            _modelPath = Path.Combine(env.ContentRootPath, "Modules", "DiseaseDetection", "Models", "rubber_leaf_disease_model.onnx");
            
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
            // Note: The new model handles Pests ("Pesta") too, so we might not need the mock check anymore.
            // But if the user selected 'Pest' (Type=1) specifically, we can still use this service 
            // if we trust the 'Pesta' class, or fallback. 
            // For now, let's allow this service to handle everything if the model supports it.

            _logger.LogInformation($"[AI] Starting ONNX Inference for {request.Type}...");

            if (_session == null)
            {
                throw new FileNotFoundException($"ONNX Model not found at {_modelPath}. Please upload it.");
            }

            // 2. Preprocess Image
            using var stream = request.Image.OpenReadStream();
            using var image = await Image.LoadAsync<Rgb24>(stream);

            // Resize to 224x224 (FastAI standard)
            image.Mutate(x => x.Resize(224, 224));

            // 3. Create Tensor (1, 3, 224, 224) -> NCHW format for PyTorch/FastAI
            var denseTensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 });

            // ImageNet Stats
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

                        // Normalize: (Pixel/255 - Mean) / Std
                        denseTensor[0, 0, y, x] = ((pixel.R / 255f) - mean[0]) / std[0]; // R
                        denseTensor[0, 1, y, x] = ((pixel.G / 255f) - mean[1]) / std[1]; // G
                        denseTensor[0, 2, y, x] = ((pixel.B / 255f) - mean[2]) / std[2]; // B
                    }
                }
            });

            // 4. Run Inference
            // FastAI ONNX export usually names input 'input' and output 'logits' (or 'output')
            // Using session metadata to be safe
            var inputName = _session.InputMetadata.Keys.First(); 
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, denseTensor)
            };

            using var results = _session.Run(inputs);
            // Output is Raw Logits
            var outputLogits = results.First().AsTensor<float>().ToArray();

            // 5. Apply Softmax to get probabilities
            var probabilities = Softmax(outputLogits);

            // 6. Find Max
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

            // 7. Map to Result
            string predictedLabel = _labels[maxIndex];
            string remedy = GetRemedy(predictedLabel);

            _logger.LogInformation($"[AI] Prediction: {predictedLabel} ({maxScore:P2})");
            
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
            var maxLogit = logits.Max(); // Stability correction
            var exp = logits.Select(x => Math.Exp(x - maxLogit)).ToArray();
            var sum = exp.Sum();
            return exp.Select(x => (float)(x / sum)).ToArray();
        }

        private string GetRemedy(string label)
        {
            return label switch
            {
                "Anthracnose" => "Prune infected parts. Apply copper-based fungicides. Improve air circulation.",
                "Birds_eye" => "Apply fungicides like Mancozeb. Maintain proper nursery hygiene.",
                "Colletorichum" => "Similar to Anthracnose; use recommended fungicides.",
                "Corynespora" => "Serious disease. Remove fallen leaves. Apply Mancozeb or Carbendazim.",
                "Dry_Leaf" => "Check for root rot or drought stress. Ensure adequate watering.",
                "Leaf_Spot" => "Apply broad-spectrum fungicides. Avoid overhead watering.",
                "Pesta" => "Pest infestation detected. Insecticides or biological control agents may be needed.",
                "Powdery_mildew" => "Use sulfur-based dusts or wettable sulfur sprays.",
                "Healthy" => "No action needed. Maintain distinct fertilization and monitoring.",
                _ => "Consult an expert for diagnosis."
            };
        }
    }
}
