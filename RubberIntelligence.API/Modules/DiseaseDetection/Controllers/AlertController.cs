using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Modules.DiseaseDetection.Services;
using System.Security.Claims;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AlertController : ControllerBase
    {
        private readonly IAlertService _alertService;

        public AlertController(IAlertService alertService)
        {
            _alertService = alertService;
        }

        /// <summary>
        /// Get all alerts for the authenticated farmer.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAlerts([FromQuery] int limit = 50)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var alerts = await _alertService.GetUserAlertsAsync(userId.Value, limit);
            return Ok(alerts);
        }

        /// <summary>
        /// Get count of unread alerts for badge display.
        /// </summary>
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var count = await _alertService.GetUnreadCountAsync(userId.Value);
            return Ok(new { count });
        }

        /// <summary>
        /// Mark a specific alert as read.
        /// </summary>
        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            await _alertService.MarkAsReadAsync(id);
            return Ok();
        }

        private Guid? GetUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdString, out var userId) ? userId : null;
        }
    }
}
