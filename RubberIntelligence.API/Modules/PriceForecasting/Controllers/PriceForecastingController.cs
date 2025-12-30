using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Modules.PriceForecasting.DTOs;
using RubberIntelligence.API.Modules.PriceForecasting.Services;

namespace RubberIntelligence.API.Modules.PriceForecasting.Controllers
{
    [ApiController]
    [Route("api/price")]
    public class PriceForecastingController : ControllerBase
    {
        private readonly IPriceForecastingService _service;

        public PriceForecastingController(IPriceForecastingService service)
        {
            _service = service;
        }

        [HttpPost("predict")]
        public async Task<IActionResult> Predict([FromBody] PricePredictionRequest request)
        {
            if (request == null) return BadRequest("Invalid Request");

            try
            {
                var result = await _service.PredictPriceAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
