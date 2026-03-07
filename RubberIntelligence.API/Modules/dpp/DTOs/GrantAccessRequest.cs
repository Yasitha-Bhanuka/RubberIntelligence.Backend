namespace RubberIntelligence.API.Modules.Dpp.DTOs
{
    public class GrantAccessRequest
    {
        public string ExporterId { get; set; } = string.Empty;
        public string? TransactionId { get; set; }  // Optional: link to marketplace transaction
    }
}
