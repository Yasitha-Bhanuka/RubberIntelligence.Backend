using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Modules.Dpp.DTOs;
using RubberIntelligence.API.Modules.Dpp.Models;
using RubberIntelligence.API.Modules.Dpp.Services;

namespace RubberIntelligence.API.Modules.Dpp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DppController : ControllerBase
    {
        private readonly GeminiOcrService _ocrService;
        private readonly OnnxDppService _onnxDppService;
        private readonly DppEncryptionService _encryptionService;
        private readonly IWebHostEnvironment _env;

        public DppController(
            GeminiOcrService ocrService, 
            OnnxDppService onnxDppService, 
            DppEncryptionService encryptionService,
            IWebHostEnvironment env)
        {
            _ocrService = ocrService;
            _onnxDppService = onnxDppService;
            _encryptionService = encryptionService;
            _env = env;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument([FromForm] DocumentUploadRequest request)
        {
            if (request.File == null || request.File.Length == 0)
                return BadRequest("No file uploaded.");

            // 1. Save temp file
            var uploadsFolder = Path.Combine(_env.ContentRootPath, "Uploads", "Dpp");
            Directory.CreateDirectory(uploadsFolder);
            var tempPath = Path.Combine(uploadsFolder, Guid.NewGuid() + Path.GetExtension(request.File.FileName));
            
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await request.File.CopyToAsync(stream);
            }

            // 2. Perform OCR (Gemini)
            string extractedText;
            try 
            {
                using (var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
                {
                    extractedText = await _ocrService.ExtractTextAsync(fileStream, request.File.ContentType);
                }
            }
            catch (HttpRequestException ex)
            {
                // Check if it's a quota issue (Too Many Requests) or other 4xx/5xx
                if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                     return StatusCode(429, new { error = "Gemini API Quota Exceeded. Please try again later.", details = ex.Message });
                }
                
                return StatusCode((int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError), new { error = "OCR Service Failed", details = ex.Message });
            }
            catch (Exception ex)
            {
                 return StatusCode(500, new { error = "Internal Server Error during processing", details = ex.Message });
            }

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                extractedText = "No readable text found.";
            }

            // 3. Classify
            var classificationResult = _onnxDppService.ClassifyDocument(extractedText, request.File.FileName);

            // 4. Encrypt if necessary
            if (classificationResult.IsEncrypted)
            {
                var encryptedPath = tempPath + ".enc";
                await _encryptionService.EncryptFileAsync(request.File, encryptedPath);
                
                // Delete original plain file if sensitive
                if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
                
                // Keep encrypted path in DB (mocked here)
                // classificationResult.StoredPath = encryptedPath;
            }
            else
            {
                // Keep original
            }

            return Ok(classificationResult);
        }
    }
}
