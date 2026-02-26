using MongoDB.Driver.GeoJsonObjectModel;
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
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass123"),
                    Role = UserRole.Farmer,
                    PlantationName = "Green Valley Plantation",
                    Location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
                        new GeoJson2DGeographicCoordinates(80.1400, 6.5854)), // Kalutara area
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
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
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass123"),
                    Role = UserRole.Admin,
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Seed Buyer
            if (!await _userRepository.ExistsAsync("buyer@test.com"))
            {
                await _userRepository.CreateUserAsync(new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Global Buyer Inc",
                    Email = "buyer@test.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass123"),
                    Role = UserRole.Buyer,
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Seed Exporter
            if (!await _userRepository.ExistsAsync("exporter@test.com"))
            {
                await _userRepository.CreateUserAsync(new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Ceylon Exporters Ltd",
                    Email = "exporter@test.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass123"),
                    Role = UserRole.Exporter,
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Seed a second Farmer nearby (for proximity alert testing)
            if (!await _userRepository.ExistsAsync("farmer2@test.com"))
            {
                await _userRepository.CreateUserAsync(new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Kasun Perera",
                    Email = "farmer2@test.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass123"),
                    Role = UserRole.Farmer,
                    PlantationName = "Sunrise Rubber Estate",
                    Location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
                        new GeoJson2DGeographicCoordinates(80.1500, 6.5900)), // ~1km from farmer1
                    IsApproved = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
    }
}

