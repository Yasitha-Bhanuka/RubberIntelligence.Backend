using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;
using RubberIntelligence.API.Data;
using RubberIntelligence.API.Domain.Entities;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;
using RubberIntelligence.API.Modules.DiseaseDetection.Services;
using System.Security.Claims;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Require Auth
    public class DiseaseController : ControllerBase
    {
        private readonly IDiseaseDetectionService _diseaseService;
        private readonly IAlertService _alertService;
        private readonly AppDbContext _context;

        public DiseaseController(
            IDiseaseDetectionService diseaseService,
            IAlertService alertService,
            AppDbContext context)
        {
            _diseaseService = diseaseService;
            _alertService = alertService;
            _context = context;
        }

        [HttpPost("detect")]
        public async Task<IActionResult> Detect([FromForm] PredictionRequest request)
        {
            // 1. Run Prediction (Strategy: Mock or Onnx)
            var result = await _diseaseService.PredictAsync(request);

            // 2. Get User ID from Token
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid userId = Guid.TryParse(userIdString, out var parsed) ? parsed : Guid.Empty;

            // 3. Build GeoJSON location if GPS coordinates are provided
            GeoJsonPoint<GeoJson2DGeographicCoordinates>? location = null;
            if (request.Latitude.HasValue && request.Longitude.HasValue)
            {
                location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
                    new GeoJson2DGeographicCoordinates(
                        request.Longitude.Value,
                        request.Latitude.Value));
            }

            // 4. Save Record to MongoDB for Research Analysis
            var record = new DiseaseRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DiseaseType = request.Type,
                PredictedLabel = result.Label,
                Confidence = result.Confidence,
                Timestamp = DateTime.UtcNow,
                ImagePath = request.Image.FileName,
                Location = location
            };

            await _context.DiseaseRecords.InsertOneAsync(record);

            // 5. Trigger proximity alerts for nearby farmers (fire-and-forget style)
            if (!result.IsRejected && location != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _alertService.CreateProximityAlertsAsync(record);
                    }
                    catch (Exception ex)
                    {
                        // Logged inside AlertService, swallow here to not fail the detection response
                    }
                });
            }

            // 6. Return Result
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

        /// <summary>
        /// Returns all geolocated disease detections for map visualization.
        /// </summary>
        [HttpGet("map-data")]
        public async Task<IActionResult> GetMapData([FromQuery] int days = 30)
        {
            var since = DateTime.UtcNow.AddDays(-days);
            var filter = Builders<DiseaseRecord>.Filter.And(
                Builders<DiseaseRecord>.Filter.Gte(r => r.Timestamp, since),
                Builders<DiseaseRecord>.Filter.Ne(r => r.Location, null)
            );

            var detections = await _context.DiseaseRecords
                .Find(filter)
                .SortByDescending(r => r.Timestamp)
                .Limit(200)
                .ToListAsync();

            var mapData = detections.Select(d => new
            {
                id = d.Id,
                disease = d.PredictedLabel,
                latitude = d.Location!.Coordinates.Latitude,
                longitude = d.Location!.Coordinates.Longitude,
                confidence = d.Confidence,
                detectedAt = d.Timestamp,
                diseaseType = d.DiseaseType.ToString()
            });

            return Ok(mapData);
        }
    }
}

