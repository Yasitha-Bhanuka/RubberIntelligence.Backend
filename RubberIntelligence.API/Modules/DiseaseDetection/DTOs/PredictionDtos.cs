using Microsoft.AspNetCore.Http;
using RubberIntelligence.API.Modules.DiseaseDetection.Enums;

namespace RubberIntelligence.API.Modules.DiseaseDetection.DTOs
{
    public class PredictionRequest
    {
        public required IFormFile Image { get; set; }
        public required DiseaseType Type { get; set; }
    }

    public class PredictionResponse
    {
        public required string Label { get; set; }
        public double Confidence { get; set; }
        public required string Remedy { get; set; }
        public required string Severity { get; set; }
        public bool IsRejected { get; set; } = false;
        public string? RejectionReason { get; set; }
    }
}
