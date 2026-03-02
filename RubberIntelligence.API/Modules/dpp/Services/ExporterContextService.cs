using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Modules.Dpp.DTOs;

namespace RubberIntelligence.API.Modules.Dpp.Services
{
    /// <summary>
    /// Provides exporter profile context to a buyer who is reviewing an access request.
    /// Shows platform tenure, collaboration history, and verification status.
    /// </summary>
    public class ExporterContextService
    {
        private readonly IUserRepository _userRepository;
        private readonly IMarketplaceRepository _marketplaceRepository;

        public ExporterContextService(IUserRepository userRepository, IMarketplaceRepository marketplaceRepository)
        {
            _userRepository = userRepository;
            _marketplaceRepository = marketplaceRepository;
        }

        /// <summary>
        /// Builds the ExporterContextDto for a given exporterId, scoped to the requesting buyer.
        /// TotalCollaborationsWithBuyer and LastCollaborationDate are derived from shared transactions.
        /// </summary>
        public async Task<ExporterContextDto> GetExporterContext(string exporterId, string requestingBuyerId)
        {
            var exporterGuid = Guid.Parse(exporterId);
            var exporter = await _userRepository.GetByIdAsync(exporterGuid)
                ?? throw new KeyNotFoundException($"Exporter {exporterId} not found.");

            // All transactions where this exporter participated, then filter to buyer collaborations
            var allTransactions = await _marketplaceRepository.GetTransactionsByUserIdAsync(exporterId);
            var buyerCollaborations = allTransactions
                .Where(t => t.BuyerId == requestingBuyerId)
                .OrderByDescending(t => t.LastUpdatedAt)
                .ToList();

            var tenureMonths = (int)((DateTime.UtcNow - exporter.CreatedAt).TotalDays / 30.44);

            return new ExporterContextDto
            {
                Name                         = exporter.FullName,
                Country                      = exporter.Country,
                OrganizationType             = exporter.OrganizationType,
                PlatformTenureMonths         = Math.Max(0, tenureMonths),
                TotalCollaborationsWithBuyer = buyerCollaborations.Count,
                LastCollaborationDate        = buyerCollaborations.FirstOrDefault()?.LastUpdatedAt,
                IsVerified                   = exporter.IsApproved
            };
        }
    }
}
