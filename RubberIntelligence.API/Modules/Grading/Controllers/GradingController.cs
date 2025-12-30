using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Modules.Grading.DTOs;
using RubberIntelligence.API.Modules.Grading.Services;

namespace RubberIntelligence.API.Modules.Grading.Controllers
{
    [ApiController]
    [Route("api/grading")]
    public class GradingController : ControllerBase
    {
        private readonly IGradingService _service;

        public GradingController(IGradingService service)
        {
            _service = service;
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> Analyze([FromForm] GradingRequest request)
        {
            if (request.Image == null) return BadRequest("Image is required");

            try
            {
                var result = await _service.AnalyzeImageAsync(request.Image);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
