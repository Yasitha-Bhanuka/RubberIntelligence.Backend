using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;
using RubberIntelligence.API.Data;
using RubberIntelligence.API.Data.Repositories;
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
        private readonly IUserRepository _userRepository;
        private readonly AppDbContext _context;

        public DiseaseController(
            IDiseaseDetectionService diseaseService,
            IAlertService alertService,
            IUserRepository userRepository,
            AppDbContext context)
        {
            _diseaseService = diseaseService;
            _alertService = alertService;
            _userRepository = userRepository;
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

            // 3. GPS Fallback: if no coordinates in request, use user's plantation location
            if (!request.Latitude.HasValue || !request.Longitude.HasValue)
            {
                var user = await _userRepository.GetByIdAsync(userId);
                if (user?.Location != null)
                {
                    request.Latitude = user.Location.Coordinates.Latitude;
                    request.Longitude = user.Location.Coordinates.Longitude;
                }
            }

            // 4. Build GeoJSON location if GPS coordinates are available
            GeoJsonPoint<GeoJson2DGeographicCoordinates>? location = null;
            if (request.Latitude.HasValue && request.Longitude.HasValue)
            {
                location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
                    new GeoJson2DGeographicCoordinates(
                        request.Longitude.Value,
                        request.Latitude.Value));
            }

            // 5. Save Record to MongoDB for Research Analysis
            var record = new DiseaseRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DiseaseType = request.Type,
                PredictedLabel = result.Label,
                Confidence = result.Confidence,
                Severity = result.Severity,
                Timestamp = DateTime.UtcNow,
                ImagePath = request.Image.FileName,
                Location = location
            };

            await _context.DiseaseRecords.InsertOneAsync(record);

            // 6. Trigger proximity alerts only for Medium/High severity detections
            if (!result.IsRejected && location != null && IsAlertableSeverity(result.Severity))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _alertService.CreateProximityAlertsAsync(record, result.Severity);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DiseaseController] Proximity alert creation failed for detection {record.Id}: {ex.Message}");
                    }
                });
            }

            // 7. Return Result
            return Ok(result);
        }

        /// <summary>
        /// Only Medium and High severity detections should trigger proximity alerts.
        /// </summary>
        private static bool IsAlertableSeverity(string severity)
        {
            return severity.Equals("High", StringComparison.OrdinalIgnoreCase)
                || severity.Equals("Medium", StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdString, out var userId))
            {
                return Unauthorized();
            }

            try
            {
                var filter = Builders<DiseaseRecord>.Filter.And(
                    Builders<DiseaseRecord>.Filter.Eq(r => r.UserId, userId),
                    Builders<DiseaseRecord>.Filter.Ne(r => r.PredictedLabel, "Rejected"),
                    Builders<DiseaseRecord>.Filter.Ne(r => r.PredictedLabel, "Unrecognized Domain")
                );
                var history = await _context.DiseaseRecords
                                            .Find(filter)
                                            .SortByDescending(r => r.Timestamp)
                                            .Limit(20)
                                            .ToListAsync();

                return Ok(history);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DiseaseController] GetHistory error for user {userId}: {ex.Message}");
                return StatusCode(500, new { error = "Failed to load disease history", detail = ex.Message });
            }
        }

        /// <summary>
        /// Returns all geolocated disease detections for map visualization.
        /// </summary>
        [HttpGet("map-data")]
        public async Task<IActionResult> GetMapData([FromQuery] int days = 30)
        {
            try
            {
                var since = DateTime.UtcNow.AddDays(-days);

                // Use Exists + Type check instead of Ne(null) to avoid GeoJSON serialization issues
                // Exclude Rejected or Unrecognized detections from the map
                var filter = Builders<DiseaseRecord>.Filter.And(
                    Builders<DiseaseRecord>.Filter.Gte(r => r.Timestamp, since),
                    Builders<DiseaseRecord>.Filter.Exists(r => r.Location),
                    Builders<DiseaseRecord>.Filter.Type(r => r.Location, MongoDB.Bson.BsonType.Document),
                    Builders<DiseaseRecord>.Filter.Ne(r => r.PredictedLabel, "Rejected"),
                    Builders<DiseaseRecord>.Filter.Ne(r => r.PredictedLabel, "Unrecognized Domain")
                );

                var detections = await _context.DiseaseRecords
                    .Find(filter)
                    .SortByDescending(r => r.Timestamp)
                    .Limit(200)
                    .ToListAsync();

                var mapData = detections
                    .Where(d => d.Location != null) // Extra safety check after deserialization
                    .Select(d => new
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
            catch (Exception ex)
            {
                Console.WriteLine($"[DiseaseController] GetMapData error: {ex.Message}");
                return StatusCode(500, new { error = "Failed to load map data", detail = ex.Message });
            }
        }
    }
}

