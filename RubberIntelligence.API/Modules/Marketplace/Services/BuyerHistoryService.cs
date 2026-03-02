using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Modules.Marketplace.DTOs;

namespace RubberIntelligence.API.Modules.Marketplace.Services
{
    /// <summary>
    /// Analyzes a buyer's trading history across SellingPosts and DigitalProductPassports.
    /// Used by exporters on the Marketplace screen before deciding to engage with a buyer.
    /// </summary>
    public class BuyerHistoryService
    {
        private readonly IMarketplaceRepository _marketplaceRepository;
        private readonly IDppRepository _dppRepository;

        public BuyerHistoryService(IMarketplaceRepository marketplaceRepository, IDppRepository dppRepository)
        {
            _marketplaceRepository = marketplaceRepository;
            _dppRepository = dppRepository;
        }

        public async Task<BuyerHistoryDto> GetBuyerHistory(string buyerId)
        {
            var posts = await _marketplaceRepository.GetPostsByBuyerIdAsync(buyerId);

            var accepted     = posts.Count(p => p.Status is "APPROVED" or "COMPLETED" or "Sold");
            var rejected     = posts.Count(p => p.Status == "REJECTED");
            var reinspections = posts.Count(p => p.Status == "REINSPECTION");
            var lastActivity = posts.OrderByDescending(p => p.CreatedAt).FirstOrDefault()?.CreatedAt;

            // Collect DPP quality data for all posts that have a linked DPP document
            var dppLinkedIds = posts
                .Where(p => !string.IsNullOrEmpty(p.DppDocumentId))
                .Select(p => p.DppDocumentId!)
                .ToList();

            var qualityScores = new List<double>();
            foreach (var lotId in dppLinkedIds)
            {
                var dpp = await _dppRepository.GetDppByLotIdAsync(lotId);
                if (dpp != null)
                    qualityScores.Add(GradeToQualityScore(dpp.RubberGrade));
            }

            var avgQuality = qualityScores.Count > 0 ? qualityScores.Average() : 0.0;

            // VerificationConsistency = share of posts with a DPP document
            var coverage = posts.Count > 0 ? (double)dppLinkedIds.Count / posts.Count : 0.0;
            var consistency = coverage >= 0.80 ? "High" : coverage >= 0.40 ? "Medium" : "Low";

            return new BuyerHistoryDto
            {
                BuyerId                 = buyerId,
                TotalLots               = posts.Count,
                Accepted                = accepted,
                Rejected                = rejected,
                ReInspections           = reinspections,
                AverageQuality          = Math.Round(avgQuality, 1),
                VerificationConsistency = consistency,
                LastActivityDate        = lastActivity
            };
        }

        private static double GradeToQualityScore(string grade) => grade?.ToUpperInvariant() switch
        {
            "RSS1" => 100,
            "RSS2" => 90,
            "RSS3" => 80,
            "RSS4" => 70,
            "RSS5" => 60,
            _      => 75
        };
    }
}
