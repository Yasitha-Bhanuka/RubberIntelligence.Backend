using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using RubberIntelligence.API.Modules.RubberLatexQuality.DTOs;

namespace RubberIntelligence.API.Modules.RubberLatexQuality.Services
{
    public class OnnxLatexQualityService : ILatexQualityService
    {
        private readonly string _modelPath;
        private readonly InferenceSession _session;
        private readonly ILogger<OnnxLatexQualityService> _logger;

        public OnnxLatexQualityService(IWebHostEnvironment env, ILogger<OnnxLatexQualityService> logger)
        {
            _logger = logger;
            _modelPath = Path.Combine(env.ContentRootPath, "Modules", "RubberLatexQuality", "Models", "Rubber_latex_quality_Model.onnx");

            if (File.Exists(_modelPath))
            {
                try
                {
                    _session = new InferenceSession(_modelPath);
                    _logger.LogInformation($"[LatexQualityAI] ONNX Model loaded from {_modelPath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[LatexQualityAI] Failed to load ONNX model");
                }
            }
            else
            {
                _logger.LogWarning($"[LatexQualityAI] Model file not found at {_modelPath}");
            }
        }

        public async Task<LatexQualityResponse> PredictQualityAsync(LatexQualityRequest request)
        {
            if (_session == null)
                throw new InvalidOperationException("Latex Quality Model is not available.");

            try
            {
                var inputs = new List<NamedOnnxValue>();
                _logger.LogInformation("[LatexQualityAI] Preparing inputs using dynamic mapping...");

                foreach (var inputNode in _session.InputMetadata)
                {
                    var name = inputNode.Key;
                    float value = 0f;

                    if (name.Contains("temperature", StringComparison.OrdinalIgnoreCase))
                        value = (float)request.Temperature;
                    else if (name.Contains("turbidity", StringComparison.OrdinalIgnoreCase))
                        value = (float)request.Turbidity;
                    else if (name.Contains("ph", StringComparison.OrdinalIgnoreCase))
                        value = (float)request.PH;
                    else
                        _logger.LogWarning($"[LatexQualityAI] Unknown input node: {name}");

                    // Create tensor of shape [1, 1] for scalar inputs
                    var tensor = new DenseTensor<float>(new[] { value }, new[] { 1, 1 });
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, tensor));
                }

                // Run inference
                using var results = _session.Run(inputs);

                _logger.LogInformation("ONNX Inference successful. Inspecting outputs...");
                foreach (var meta in _session.OutputMetadata)
                {
                    _logger.LogInformation($"Output Meta: Name={meta.Key}, Type={meta.Value.ElementType}, Dim={string.Join(",", meta.Value.Dimensions)}");
                }

                foreach (var res in results)
                {
                    _logger.LogInformation($"Result: Name={res.Name}, Type={res.Value?.GetType().Name}, ValueType={res.ValueType}");
                }

                // Attempt to find a suitable output tensor
                DenseTensor<float> outputTensor = null;

                // First try to find a float tensor
                var floatResult = results.FirstOrDefault(r => r.Value is Tensor<float> || (r.ElementType == TensorElementType.Float));
                
                if (floatResult != null)
                {
                    outputTensor = floatResult.AsTensor<float>().ToDenseTensor();
                }
                else
                {
                    // Fallback: Check if it's Int64 (often labels) and try to use it? 
                    // Or if it's a Map (probabilities)
                    var firstRes = results.First();
                    if (firstRes.ElementType == TensorElementType.Int64)
                    {
                         _logger.LogWarning("Found Int64 output, converting to float for compatibility check.");
                         var intTensor = firstRes.AsTensor<long>();
                         // This is likely a class label, not a probability distribution
                         // We will log and throw for now to see the logs
                    }
                }

                if (outputTensor == null) {
                     _logger.LogError("Could not find a float output tensor provided by the model.");
                     throw new InvalidOperationException("Model output format not supported yet. Check logs for details.");
                }

                var outputArray = outputTensor.ToArray();

                // Process model output
                // If model outputs a single value, use it as quality score
                // If model outputs multiple classes, find the max probability
                float qualityValue;
                double confidence;

                if (outputArray.Length == 1)
                {
                    // Single output - treat as quality score (0-100)
                    qualityValue = outputArray[0];
                    confidence = Math.Min(1.0, Math.Abs(qualityValue) / 100.0);
                }
                else
                {
                    // Multiple outputs - find max probability
                    int maxIndex = 0;
                    float maxProb = outputArray[0];
                    for (int i = 1; i < outputArray.Length; i++)
                    {
                        if (outputArray[i] > maxProb)
                        {
                            maxProb = outputArray[i];
                            maxIndex = i;
                        }
                    }
                    qualityValue = maxProb * 100;
                    confidence = maxProb;
                }

                // Convert to quality score (0-100)
                int qualityScore = Math.Max(0, Math.Min(100, (int)Math.Round(qualityValue)));

                // Determine quality grade and status based on sensor rules
                var (grade, status) = DetermineQualityRules(request.Temperature, request.Turbidity, request.PH);

                // Generate recommendations
                var recommendations = GenerateRecommendations(request, grade);

                _logger.LogInformation($"[LatexQualityAI] Prediction: {grade} (Rule-Based), Model Score: {qualityScore}, Confidence: {confidence:P2}");

                return new LatexQualityResponse
                {
                    QualityGrade = grade,
                    Confidence = confidence,
                    QualityScore = qualityScore,
                    Status = status,
                    Recommendations = recommendations,
                    SensorReadings = new SensorReadings
                    {
                        Temperature = request.Temperature,
                        Turbidity = request.Turbidity,
                        PH = request.PH
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LatexQualityAI] Error during prediction");
                throw;
            }
        }

        private (string Grade, string Status) DetermineQualityRules(double temp, double turbidity, double ph)
        {
            // First check if ANY parameter is Poor
            bool tempPoor = temp < 20 || temp > 32; 
            bool turbPoor = turbidity > -1500;
            bool phPoor = ph < 5.2 || ph > 7.2;

            if (tempPoor || turbPoor || phPoor)
            {
                return ("Poor Quality", "Fail");
            }

            // Check if ALL are Excellent
            bool tempExc = temp >= 26 && temp <= 32;
            bool turbExc = turbidity <= -5500;
            bool phExc = ph >= 6.9 && ph <= 7.2;

            if (tempExc && turbExc && phExc)
            {
                return ("Excellent Quality", "Pass");
            }

            // Check if ALL are at least Good
            bool tempGood = temp >= 24 && temp <= 32;
            bool turbGood = turbidity <= -2500;
            bool phGood = ph >= 5.8 && ph <= 7.2;

            if (tempGood && turbGood && phGood)
            {
                return ("Good Quality", "Pass");
            }

            // Check if ALL are at least Average
            bool tempAvg = temp >= 20 && temp <= 32;
            bool turbAvg = turbidity <= -1500;
            bool phAvg = ph >= 5.2 && ph <= 7.2;

            if (tempAvg && turbAvg && phAvg)
            {
                return ("Average Quality", "Warning");
            }

            // Fallback
            return ("Very Bad Quality", "Fail");
        }

        private string[] GenerateRecommendations(LatexQualityRequest request, string grade)
        {
            var recommendations = new List<string>();

            // Temperature recommendations
            if (request.Temperature < 27)
                recommendations.Add("Temperature is below optimal range. Consider warming the latex to 27-32°C.");
            else if (request.Temperature > 32)
                recommendations.Add("Temperature is above optimal range. Cool the latex to 27-32°C range.");

            // Turbidity recommendations
            if (request.Turbidity > -3500)
                recommendations.Add("Turbidity levels are not optimal. Ensure proper filtration and settling time.");

            // pH recommendations
            if (request.PH < 6.5)
                recommendations.Add("pH is too low. Add pH stabilizer to maintain 6.5-7.2 range.");
            else if (request.PH > 7.2)
                recommendations.Add("pH is too high. Monitor ammonia levels and adjust accordingly.");

            // Overall quality recommendations
            if (string.Equals(grade, "Excellent Quality", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(grade, "Good Quality", StringComparison.OrdinalIgnoreCase))
            {
                recommendations.Add("Good quality latex! Maintain current processing conditions.");
            }
            else if (string.Equals(grade, "Average Quality", StringComparison.OrdinalIgnoreCase))
            {
                recommendations.Add("Average quality latex. Minor adjustments may improve consistency.");
            }
            else if (string.Equals(grade, "Poor Quality", StringComparison.OrdinalIgnoreCase))
            {
                recommendations.Add("Poor quality latex. Immediate corrective action required.");
                recommendations.Add("Consult with quality control team to identify root causes.");
            }
            else
            {
                recommendations.Add("Quality is below acceptable standards. rubber latex sample is not suitable for processing.");
            }

            return recommendations.ToArray();
        }
    }
}
