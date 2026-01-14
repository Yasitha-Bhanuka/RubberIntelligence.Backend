using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Modules.RubberLatexQuality.DTOs;
using RubberIntelligence.API.Modules.RubberLatexQuality.Services;

namespace RubberIntelligence.API.Modules.RubberLatexQuality.Controllers
{
    [ApiController]
    [Route("api/latex-quality")]
    public class LatexQualityController : ControllerBase
    {
        private readonly ILatexQualityService _service;
        private readonly ILogger<LatexQualityController> _logger;

        public LatexQualityController(ILatexQualityService service, ILogger<LatexQualityController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpPost("predict")]
        public async Task<IActionResult> Predict([FromBody] LatexQualityRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required");

            // Validate sensor data ranges
            if (request.Temperature <= 0 || request.Temperature > 100)
                return BadRequest("Invalid temperature value");

            if (request.PH <= 0 || request.PH > 14)
                return BadRequest("Invalid pH value");

            try
            {
                _logger.LogInformation($"[LatexQuality] Predicting quality for T:{request.Temperature}°C, Turb:{request.Turbidity}, pH:{request.PH}");
                
                var result = await _service.PredictQualityAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LatexQuality] Prediction failed");
                return StatusCode(500, new { message = "Prediction failed", error = ex.Message });
            }
        }
    }
}
