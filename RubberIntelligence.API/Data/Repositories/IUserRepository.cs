using RubberIntelligence.API.Domain.Entities;

namespace RubberIntelligence.API.Data.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByEmailAsync(string email);
        Task CreateUserAsync(User user);
        Task<bool> ExistsAsync(string email);
    }
}
