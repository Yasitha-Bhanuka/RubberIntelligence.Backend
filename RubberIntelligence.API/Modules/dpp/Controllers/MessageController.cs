using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Modules.Dpp.DTOs;
using RubberIntelligence.API.Modules.Dpp.Services;
using System.Security.Claims;

namespace RubberIntelligence.API.Modules.Dpp.Controllers
{
    /// <summary>
    /// Lot-Linked Secure Messaging.
    /// POST /api/messages/{lotId} — Send a message (optionally confidential, AES-256 encrypted).
    /// GET  /api/messages/{lotId} — Retrieve all messages for a lot (only for participants).
    /// Encryption and decryption happen exclusively in MessageService.
    /// </summary>
    [ApiController]
    [Route("api/messages")]
    [Authorize]
    public class MessageController : ControllerBase
    {
        private readonly MessageService _messageService;
        private readonly ILogger<MessageController> _logger;

        public MessageController(MessageService messageService, ILogger<MessageController> logger)
        {
            _messageService = messageService;
            _logger         = logger;
        }

        // ── POST /api/messages/{lotId} ───────────────────────────────────
        [HttpPost("{lotId}")]
        public async Task<IActionResult> SendMessage(string lotId, [FromBody] SendMessageRequest request)
        {
            var senderId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(senderId)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Content))
                return BadRequest(new { error = "Message content cannot be empty." });

            if (string.IsNullOrWhiteSpace(request.ReceiverId))
                return BadRequest(new { error = "ReceiverId is required." });

            try
            {
                var result = await _messageService.SendMessage(
                    lotId, senderId, request.ReceiverId, request.Content, request.IsConfidential);

                _logger.LogInformation(
                    "[MSG] {SenderId} → {ReceiverId} | lot={LotId} | confidential={Conf}",
                    senderId, request.ReceiverId, lotId, request.IsConfidential);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MSG] Send failed for lot {LotId}", lotId);
                return StatusCode(500, new { error = "Failed to send message", details = ex.Message });
            }
        }

        // ── GET /api/messages/unread-count ──────────────────────────
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var count = await _messageService.GetUnreadCount(userId);
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MSG] Unread count failed for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to get unread count", details = ex.Message });
            }
        }

        // ── GET /api/messages/{lotId} ────────────────────────────────────
        [HttpGet("{lotId}")]
        public async Task<IActionResult> GetMessages(string lotId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var messages = await _messageService.GetMessages(lotId, userId);
                return Ok(messages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MSG] Fetch failed for lot {LotId}", lotId);
                return StatusCode(500, new { error = "Failed to retrieve messages", details = ex.Message });
            }
        }
    }
}
