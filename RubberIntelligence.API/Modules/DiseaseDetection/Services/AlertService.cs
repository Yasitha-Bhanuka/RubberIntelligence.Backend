using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RubberIntelligence.API.Data;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Domain.Entities;
using RubberIntelligence.API.Modules.DiseaseDetection.Models;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class AlertService : IAlertService
    {
        private readonly AppDbContext _context;
        private readonly IUserRepository _userRepository;
        private readonly AlertSettings _alertSettings;
        private readonly ILogger<AlertService> _logger;

        public AlertService(
            AppDbContext context,
            IUserRepository userRepository,
            IOptions<AlertSettings> alertSettings,
            ILogger<AlertService> logger)
        {
            _context = context;
            _userRepository = userRepository;
            _alertSettings = alertSettings.Value;
            _logger = logger;
        }

        public async Task CreateProximityAlertsAsync(DiseaseRecord detection, string severity)
        {
            if (detection.Location == null)
            {
                _logger.LogWarning("Disease detection {Id} has no location, skipping proximity alerts", detection.Id);
                return;
            }

            var longitude = detection.Location.Coordinates.Longitude;
            var latitude = detection.Location.Coordinates.Latitude;
            var radiusMeters = _alertSettings.RadiusInKm * 1000;

            try
            {
                var nearbyFarmers = await _userRepository.GetNearbyFarmersAsync(
                    longitude, latitude, radiusMeters);

                // Exclude the farmer who detected the disease
                var farmersToAlert = nearbyFarmers
                    .Where(f => f.Id != detection.UserId && f.Location != null)
                    .ToList();

                if (!farmersToAlert.Any())
                {
                    _logger.LogInformation("No nearby farmers found within {Radius}km for detection {Id}",
                        _alertSettings.RadiusInKm, detection.Id);
                    return;
                }

                var alerts = farmersToAlert.Select(farmer => new Alert
                {
                    Id = Guid.NewGuid(),
                    FarmerId = farmer.Id,
                    DetectionId = detection.Id,
                    DiseaseName = detection.PredictedLabel,
                    DistanceKm = CalculateDistanceKm(
                        latitude, longitude,
                        farmer.Location!.Coordinates.Latitude,
                        farmer.Location!.Coordinates.Longitude),
                    Latitude = latitude,
                    Longitude = longitude,
                    CreatedAt = DateTime.UtcNow,
                    Severity = severity,
                    IsRead = false
                }).ToList();

                await _context.Alerts.InsertManyAsync(alerts);

                _logger.LogInformation("Created {Count} proximity alerts for detection {Id} ({Disease})",
                    alerts.Count, detection.Id, detection.PredictedLabel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create proximity alerts for detection {Id}", detection.Id);
            }
        }

        public async Task<List<Alert>> GetUserAlertsAsync(Guid farmerId, int limit = 50)
        {
            var filter = Builders<Alert>.Filter.Eq(a => a.FarmerId, farmerId);
            return await _context.Alerts
                .Find(filter)
                .SortByDescending(a => a.CreatedAt)
                .Limit(limit)
                .ToListAsync();
        }

        public async Task MarkAsReadAsync(Guid alertId)
        {
            var update = Builders<Alert>.Update.Set(a => a.IsRead, true);
            await _context.Alerts.UpdateOneAsync(a => a.Id == alertId, update);
        }

        public async Task<int> GetUnreadCountAsync(Guid farmerId)
        {
            var filter = Builders<Alert>.Filter.And(
                Builders<Alert>.Filter.Eq(a => a.FarmerId, farmerId),
                Builders<Alert>.Filter.Eq(a => a.IsRead, false)
            );

            return (int)await _context.Alerts.CountDocumentsAsync(filter);
        }

        /// <summary>
        /// Haversine formula to calculate distance between two GPS coordinates.
        /// </summary>
        private static double CalculateDistanceKm(
            double lat1, double lon1,
            double lat2, double lon2)
        {
            const double R = 6371; // Earth's radius in km
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return Math.Round(R * c, 2);
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    }
}
