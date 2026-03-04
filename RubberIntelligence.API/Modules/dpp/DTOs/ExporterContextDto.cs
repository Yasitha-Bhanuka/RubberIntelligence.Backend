namespace RubberIntelligence.API.Modules.Dpp.DTOs
{
    public class ExporterContextDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? OrganizationType { get; set; }
        public int PlatformTenureMonths { get; set; }
        public int TotalCollaborationsWithBuyer { get; set; }
        public DateTime? LastCollaborationDate { get; set; }
        public bool IsVerified { get; set; }
    }
}
