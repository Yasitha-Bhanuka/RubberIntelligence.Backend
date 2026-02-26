using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    /// <summary>
    /// Orchestrates all image validation checks (quality + content) before
    /// the image is sent to the disease detection AI models.
    /// </summary>
    public class ImageValidationService : IImageValidationService
    {
        private readonly ImageQualityService _qualityService;
        private readonly ContentVerificationService _contentService;
        private readonly IConfiguration _config;
        private readonly ILogger<ImageValidationService> _logger;

        public ImageValidationService(
            ImageQualityService qualityService,
            ContentVerificationService contentService,
            IConfiguration config,
            ILogger<ImageValidationService> logger)
        {
            _qualityService = qualityService;
            _contentService = contentService;
            _config = config;
            _logger = logger;
        }

        public async Task<ImageValidationResult> ValidateAsync(PredictionRequest request)
        {
            _logger.LogInformation("[Validation] Starting image validation for type={Type}...", request.Type);

            var result = new ImageValidationResult { IsValid = true };

            // ── Step 1: Image Quality Check (blur + resolution) ──────────────
            using var qualityStream = request.Image.OpenReadStream();
            var qualityResult = await _qualityService.CheckQualityAsync(qualityStream);
            result.QualityResult = qualityResult;

            if (!qualityResult.IsAcceptable)
            {
                result.IsValid = false;
                result.RejectReason = qualityResult.RejectReason;
                _logger.LogWarning("[Validation] Rejected at quality check: {Reason}", result.RejectReason);
                return result;
            }

            // ── Step 2: Content Verification (is this a leaf/pest/weed?) ─────
            var enableContentCheck = _config.GetValue<bool>("ImageValidation:EnableContentVerification", true);

            if (enableContentCheck)
            {
                // Need to reopen the stream since the quality check consumed it
                using var contentStream = request.Image.OpenReadStream();
                var contentResult = await _contentService.VerifyContentAsync(contentStream, request.Type);
                result.ContentResult = contentResult;

                if (!contentResult.IsContentValid)
                {
                    result.IsValid = false;
                    result.RejectReason = contentResult.RejectReason;
                    _logger.LogWarning("[Validation] Rejected at content check: {Reason}", result.RejectReason);
                    return result;
                }
            }
            else
            {
                _logger.LogInformation("[Validation] Content verification disabled via config.");
            }

            _logger.LogInformation("[Validation] Image passed all validation checks.");
            return result;
        }
    }
}
