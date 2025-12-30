using RubberIntelligence.API.Domain.Enums;

namespace RubberIntelligence.API.Domain.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public required string FullName { get; set; }
        public required string Email { get; set; }
        public string? PasswordHash { get; set; }
        public UserRole Role { get; set; }
    }
}
