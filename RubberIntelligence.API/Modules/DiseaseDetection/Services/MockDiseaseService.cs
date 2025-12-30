using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class MockDiseaseService : IDiseaseDetectionService
    {
        public Task<PredictionResponse> PredictAsync(PredictionRequest request)
        {
            // Simulate processing time
            // In future: Save 'request.Image' to disk/cloud and pass path to Python/ONNX

            PredictionResponse response;

            switch (request.Type)
            {
                case DiseaseType.LeafDisease:
                    response = new PredictionResponse
                    {
                        Label = "White Root Disease (Rigidoporus microporus)",
                        Confidence = 0.92,
                        Severity = "High",
                        Remedy = "Apply fungicides (e.g., hexaconazole) around the root collar. Isolate infected trees."
                    };
                    break;

                case DiseaseType.Pest:
                    response = new PredictionResponse
                    {
                        Label = "Rubber Termite",
                        Confidence = 0.88,
                        Severity = "Medium",
                        Remedy = "Use chemical barriers (chlorpyrifos) and destroy termite mounds near the plantation."
                    };
                    break;

                case DiseaseType.Weed:
                    response = new PredictionResponse
                    {
                        Label = "Chromolaena odorata",
                        Confidence = 0.95,
                        Severity = "Low",
                        Remedy = "Manual uprooting or application of glyphosate-based herbicides."
                    };
                    break;

                default:
                    response = new PredictionResponse
                    {
                        Label = "Healthy",
                        Confidence = 0.99,
                        Severity = "None",
                        Remedy = "Continue regular maintenance."
                    };
                    break;
            }

            return Task.FromResult(response);
        }
    }
}
