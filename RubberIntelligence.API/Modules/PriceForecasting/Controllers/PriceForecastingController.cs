using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Modules.PriceForecasting.DTOs;
using RubberIntelligence.API.Modules.PriceForecasting.Services;


//This controller receives API requests related to rubber price forecasting, sends them to the service layer, and returns the results to the client.

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

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            try
            {
                var history = await _service.GetPriceHistoryAsync();
                return Ok(history);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
