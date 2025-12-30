using Microsoft.AspNetCore.Http;

namespace RubberIntelligence.API.Modules.Grading.DTOs
{
    public class GradingRequest
    {
        public IFormFile Image { get; set; }
    }
}
