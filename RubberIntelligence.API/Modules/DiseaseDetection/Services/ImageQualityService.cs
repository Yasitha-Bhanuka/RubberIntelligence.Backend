using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using RubberIntelligence.API.Modules.DiseaseDetection.DTOs;

namespace RubberIntelligence.API.Modules.DiseaseDetection.Services
{
    public class ImageQualityService
    {
        private readonly ILogger<ImageQualityService> _logger;
        private readonly IConfiguration _config;

        // Laplacian 3x3 kernel for edge detection (blur measurement)
        private static readonly float[,] LaplacianKernel = {
            { 0, 1, 0 },
            { 1, -4, 1 },
            { 0, 1, 0 }
        };

        public ImageQualityService(ILogger<ImageQualityService> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public async Task<ImageQualityResult> CheckQualityAsync(Stream imageStream)
        {
            var minWidth = _config.GetValue<int>("ImageValidation:MinWidth", 224);
            var minHeight = _config.GetValue<int>("ImageValidation:MinHeight", 224);
            var blurThreshold = _config.GetValue<double>("ImageValidation:BlurThreshold", 100.0);

            using var image = await Image.LoadAsync<Rgba32>(imageStream);

            var result = new ImageQualityResult
            {
                Width = image.Width,
                Height = image.Height,
                IsAcceptable = true
            };

            // 1. Resolution Check
            if (image.Width < minWidth || image.Height < minHeight)
            {
                result.IsAcceptable = false;
                result.RejectReason = $"Image resolution too low ({image.Width}x{image.Height}). Minimum required: {minWidth}x{minHeight} pixels.";
                _logger.LogWarning("[Validation] Image rejected: {Reason}", result.RejectReason);
                return result;
            }

            // 2. Blur Detection via Laplacian Variance
            var blurScore = ComputeLaplacianVariance(image);
            result.BlurScore = blurScore;

            if (blurScore < blurThreshold)
            {
                result.IsAcceptable = false;
                result.RejectReason = $"Image is too blurry (blur score: {blurScore:F1}, minimum: {blurThreshold:F1}). Please take a clearer photo.";
                _logger.LogWarning("[Validation] Image rejected: {Reason}", result.RejectReason);
                return result;
            }

            _logger.LogInformation("[Validation] Image quality OK — Resolution: {W}x{H}, Blur Score: {Blur:F1}", 
                image.Width, image.Height, blurScore);
            return result;
        }

        /// <summary>
        /// Computes the Laplacian variance of the image as a measure of sharpness.
        /// Higher values = sharper image, lower values = blurrier image.
        /// </summary>
        private double ComputeLaplacianVariance(Image<Rgba32> image)
        {
            // Resize to a manageable size for blur computation (performance)
            using var grayscale = image.Clone(ctx => ctx.Resize(256, 256));

            int width = grayscale.Width;
            int height = grayscale.Height;

            // Convert to grayscale intensity array
            var intensity = new float[height, width];
            grayscale.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        var p = row[x];
                        // Standard luminance formula
                        intensity[y, x] = (0.299f * p.R + 0.587f * p.G + 0.114f * p.B) / 255f;
                    }
                }
            });

            // Apply Laplacian kernel and compute variance
            var laplacianValues = new List<double>();

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    float sum = 0;
                    for (int ky = -1; ky <= 1; ky++)
                    {
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            sum += intensity[y + ky, x + kx] * LaplacianKernel[ky + 1, kx + 1];
                        }
                    }
                    laplacianValues.Add(sum);
                }
            }

            // Variance = E[X^2] - (E[X])^2
            double mean = laplacianValues.Average();
            double variance = laplacianValues.Average(v => (v - mean) * (v - mean));

            // Scale up for more intuitive thresholding (multiply by 10000)
            return variance * 10000;
        }
    }
}
