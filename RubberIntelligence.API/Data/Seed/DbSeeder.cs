using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Domain.Entities;
using RubberIntelligence.API.Domain.Enums;

namespace RubberIntelligence.API.Data.Seed
{
    public class DbSeeder
    {
        private readonly IUserRepository _userRepository;

        public DbSeeder(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task SeedAsync()
        {
            // Seed Farmer
            if (!await _userRepository.ExistsAsync("farmer@test.com"))
            {
                await _userRepository.CreateUserAsync(new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "John Planter",
                    Email = "farmer@test.com",
                    PasswordHash = "pass123", // In prod, hash this!
                    Role = UserRole.Farmer
                });
            }

            // Seed Admin
            if (!await _userRepository.ExistsAsync("admin@test.com"))
            {
                await _userRepository.CreateUserAsync(new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Admin User",
                    Email = "admin@test.com",
                    PasswordHash = "pass123",
                    Role = UserRole.Admin
                });
            }
        }
    }
}
