namespace RubberIntelligence.API.Modules.Dpp.DTOs
{
    public class DppVerificationResponseDto
    {
        public bool IsValid { get; set; }
        public string RecalculatedHash { get; set; } = string.Empty;
        public string StoredHash { get; set; } = string.Empty;
    }
}
