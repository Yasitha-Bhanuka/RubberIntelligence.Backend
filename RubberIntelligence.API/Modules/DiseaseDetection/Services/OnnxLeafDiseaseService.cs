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
        private readonly string[] _labels = { "Anthracnose", "Dry_Leaf", "Healthy", "Leaf_Spot" };
        private readonly ILogger<OnnxLeafDiseaseService> _logger;

        public OnnxLeafDiseaseService(IWebHostEnvironment env, ILogger<OnnxLeafDiseaseService> logger)
        {
            _logger = logger;
            _modelPath = Path.Combine(env.ContentRootPath, "Modules", "DiseaseDetection", "Models", "rubber_leaf_disease_model.onnx");
            
            // Only load session if file exists (Output useful error if not)
            if (File.Exists(_modelPath))
            {
                _session = new InferenceSession(_modelPath);
                _logger.LogInformation($"[AI] ONNX Model loaded successfully from {_modelPath}");
            }
            else
            {
                _logger.LogError($"[AI] ONNX Model NOT FOUND at {_modelPath}");
            }
        }

        public async Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
            // 1. Fallback for Pest/Weed (Since model is Leaf only)
            if (request.Type != DiseaseType.LeafDisease)
            {
                return await new MockDiseaseService().PredictAsync(request);
            }

            _logger.LogInformation("[AI] Starting ONNX Inference for Leaf Disease...");

            if (_session == null)
            {
                throw new FileNotFoundException($"ONNX Model not found at {_modelPath}. Please upload it.");
            }

            // 2. Preprocess Image
            using var stream = request.Image.OpenReadStream();
            using var image = await Image.LoadAsync<Rgb24>(stream);

            // Resize to 224x224
            image.Mutate(x => x.Resize(224, 224));

            // 3. Create Tensor (1, 224, 224, 3)
            var denseTensor = new DenseTensor<float>(new[] { 1, 224, 224, 3 });
            
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++)
                    {
                        var pixel = pixelRow[x];
                        // Normalize: (Pixel / 127.5) - 1  => Range [-1, 1]
                        denseTensor[0, y, x, 0] = (pixel.R / 127.5f) - 1.0f;
                        denseTensor[0, y, x, 1] = (pixel.G / 127.5f) - 1.0f;
                        denseTensor[0, y, x, 2] = (pixel.B / 127.5f) - 1.0f;
                    }
                }
            });

            // 4. Run Inference
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_layer", denseTensor) // Named 'input_layer' from conversion script
            };

            using var results = _session.Run(inputs);
            var output = results.First().AsTensor<float>().ToArray();

            // 5. Post-Process (ArgMax to find class)
            int maxIndex = 0;
            float maxScore = -1;
            for (int i = 0; i < output.Length; i++)
            {
                if (output[i] > maxScore)
                {
                    maxScore = output[i];
                    maxIndex = i;
                }
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

        private string GetRemedy(string label)
        {
            return label switch
            {
                "Anthracnose" => "Prune infected parts and apply copper-based fungicides. Improve air circulation.",
                "Dry_Leaf" => "Ensure adequate watering and check for root issues. Mulch to retain moisture.",
                "Leaf_Spot" => "Apply fungicides like Mancozeb. Avoid overhead irrigation to reduce spread.",
                "Healthy" => "No action needed. Maintain regular fertilization schedule.",
                _ => "Consult an expert."
            };
        }
    }
}
