using RubberIntelligence.API.Domain.Entities;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public interface IAlertService
    {
        /// <summary>
        /// Finds nearby farmers and creates proximity alerts for a detected disease.
        /// </summary>
        Task CreateProximityAlertsAsync(DiseaseRecord detection, string severity);

        /// <summary>
        /// Gets all alerts for a specific farmer, ordered by most recent.
        /// </summary>
        Task<List<Alert>> GetUserAlertsAsync(Guid farmerId, int limit = 50);

        /// <summary>
        /// Marks an alert as read.
        /// </summary>
        Task MarkAsReadAsync(Guid alertId);

        /// <summary>
        /// Gets the count of unread alerts for a farmer.
        /// </summary>
        Task<int> GetUnreadCountAsync(Guid farmerId);
    }
}
