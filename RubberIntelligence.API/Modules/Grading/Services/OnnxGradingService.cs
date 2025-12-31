using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using RubberIntelligence.API.Modules.Grading.DTOs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RubberIntelligence.API.Modules.Grading.Services
{
    public class OnnxGradingService : IGradingService
    {
        private readonly string _modelPath;
        private readonly InferenceSession _session;
        private readonly ILogger<OnnxGradingService> _logger;
        
        // Classes from Python Notebook:
        // ['Good Quality with No Defects', 'Pin Head Bubbles Defect', 'Reaper Marks Defect']
        private readonly string[] _labels = new[] 
        { 
            "Good Quality with No Defects", 
            "Pin Head Bubbles Defect", 
            "Reaper Marks Defect" 
        };

        public OnnxGradingService(IWebHostEnvironment env, ILogger<OnnxGradingService> logger)
        {
            _logger = logger;
            _modelPath = Path.Combine(env.ContentRootPath, "Modules", "Grading", "Models", "rubber_grading_model.onnx");

            if (File.Exists(_modelPath))
            {
                try
                {
                    _session = new InferenceSession(_modelPath);
                    _logger.LogInformation($"[GradingAI] ONNX Model loaded from {_modelPath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GradingAI] Failed to load ONNX model");
                }
            }
            else
            {
                _logger.LogWarning($"[GradingAI] Model file not found at {_modelPath}");
            }
        }

        public async Task<GradingResponse> AnalyzeImageAsync(IFormFile image)
        {
            if (_session == null)
                throw new InvalidOperationException("Grading Model is not available.");

            if (image == null || image.Length == 0)
                throw new ArgumentException("No image provided.");

            try
            {
                using var stream = image.OpenReadStream();
                using var img = Image.Load<Rgb24>(stream);

                // 1. Resize to 224x224
                img.Mutate(x => x.Resize(224, 224));

                // 2. Preprocess & Create Tensor
                // MobileNetV2 expects values in [-1, 1] => (pixel / 127.5) - 1
                var input = new DenseTensor<float>(new[] { 1, 224, 224, 3 });
                img.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var pixelRow = accessor.GetRowSpan(y);
                        for (int x = 0; x < accessor.Width; x++)
                        {
                            var pixel = pixelRow[x];
                            // Normalized: (x - 127.5) / 127.5
                            input[0, y, x, 0] = (pixel.R / 127.5f) - 1f;
                            input[0, y, x, 1] = (pixel.G / 127.5f) - 1f;
                            input[0, y, x, 2] = (pixel.B / 127.5f) - 1f;
                        }
                    }
                });

                // 3. Run Inference
                // Note: TFLite converted models usually have input name like 'input_1' or 'serving_default_input_1:0'
                // We'll use the first input from the session metadata to be safe
                var inputName = _session.InputMetadata.Keys.First();
                var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, input) };

                using var results = _session.Run(inputs);

                // 4. Extract Output
                // Output is typically Softmax probabilities [1, 3]
                var output = results.First().AsTensor<float>();
                var probabilities = output.ToArray();

                // Find Max
                int maxIndex = 0;
                float maxScore = 0f;
                for (int i = 0; i < probabilities.Length; i++)
                {
                    if (probabilities[i] > maxScore)
                    {
                        maxScore = probabilities[i];
                        maxIndex = i;
                    }
                }

                var predictedLabel = _labels[maxIndex];

                _logger.LogInformation($"[GradingAI] Result: {predictedLabel} ({maxScore:P2})");

                return new GradingResponse
                {
                    PredictedClass = predictedLabel,
                    Confidence = maxScore,
                    Severity = GetSeverity(predictedLabel),
                    Suggestions = GetSuggestion(predictedLabel)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing grading image");
                throw;
            }
        }

        private string GetSeverity(string label)
        {
            if (label.Contains("Good")) return "None";
            if (label.Contains("Pin Head")) return "Medium";
            if (label.Contains("Reaper")) return "High";
            return "Unknown";
        }

        private string GetSuggestion(string label)
        {
            if (label.Contains("Good")) return "High quality sheet. Store in a dry, ventilated area.";
            if (label.Contains("Pin Head")) return "Caused by gas formation during coagulation. Use clean water and proper acid dosage.";
            if (label.Contains("Reaper")) return "Physical damage during processing. Check rollers and handling equipment.";
            return "Contact an expert.";
        }
    }
}
