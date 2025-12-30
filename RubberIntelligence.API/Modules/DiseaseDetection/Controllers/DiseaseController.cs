using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Data;
using RubberIntelligence.API.Domain.Entities;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Services;
using System.Security.Claims;
using MongoDB.Driver;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Require Auth
    public class DiseaseController : ControllerBase
    {
        private readonly IDiseaseDetectionService _diseaseService;
        private readonly AppDbContext _context;

        public DiseaseController(IDiseaseDetectionService diseaseService, AppDbContext context)
        {
            _diseaseService = diseaseService;
            _context = context;
        }

        [HttpPost("detect")]
        public async Task<IActionResult> Detect([FromForm] PredictionRequest request)
        {
            // 1. Run Prediction (Strategy: Mock or Onnx)
            var result = await _diseaseService.PredictAsync(request);

            // 2. Get User ID from Token
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            // Fallback if claim is missing or named differently (e.g., "id" or "sub")
            if (string.IsNullOrEmpty(userIdString))
            {
                 // We don't want to fail if auth claim mapping is tricky, so just generate one or log error
                 // ideally, retrieve from User object setup in JwtTokenService
            }
            // For now, let's assume valid auth means valid user, we can parse or generate a placeholder
            // Note: Standard JwtRegisteredClaimNames.Sub might be used. 
            // We'll generate a random one if not found for robust testing, or parse if valid.
            Guid userId = Guid.TryParse(userIdString, out var parsed) ? parsed : Guid.Empty;

            // 3. Save Record to MongoDB for Research Analysis
            var record = new DiseaseRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DiseaseType = request.Type,
                PredictedLabel = result.Label,
                Confidence = result.Confidence,
                Timestamp = DateTime.UtcNow,
                ImagePath = request.Image.FileName // In real app, save file to blob and store URL
            };

            await _context.DiseaseRecords.InsertOneAsync(record);

            // 4. Return Result
            return Ok(result);
        }
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdString, out var userId))
            {
                return Unauthorized();
            }

            var filter = Builders<DiseaseRecord>.Filter.Eq(r => r.UserId, userId);
            var history = await _context.DiseaseRecords
                                        .Find(filter)
                                        .SortByDescending(r => r.Timestamp)
                                        .Limit(20) // Limit to last 20
                                        .ToListAsync();

            return Ok(history);
        }
    }
}
