using Microsoft.AspNetCore.Http;

namespace RubberIntelligence.API.Modules.Dpp.DTOs
{
    public class DocumentUploadRequest
    {
        public IFormFile File { get; set; } = null!;
        public string? Notes { get; set; }
    }
}
