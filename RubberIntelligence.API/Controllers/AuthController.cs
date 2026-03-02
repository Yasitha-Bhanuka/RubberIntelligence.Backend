using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Domain.Entities;
using RubberIntelligence.API.Domain.Enums;
using RubberIntelligence.API.Infrastructure.Security;
using System.Security.Claims;

namespace RubberIntelligence.API.Controllers
{
    public class LoginDto
    {
        public required string Email { get; set; }
        public required string Password { get; set; }
    }

    public class RegisterDto
    {
        public required string FullName { get; set; }
        public required string Email { get; set; }
        public required string Password { get; set; }
        public required string Role { get; set; }
        public string? PlantationName { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class UpdateProfileDto
    {
        public string? FullName { get; set; }
        public string? Password { get; set; }
        public string? PlantationName { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly JwtTokenService _jwtTokenService;
        private readonly IUserRepository _userRepository;

        public AuthController(JwtTokenService jwtTokenService, IUserRepository userRepository)
        {
            _jwtTokenService = jwtTokenService;
            _userRepository = userRepository;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            try
            {
                var user = await _userRepository.GetByEmailAsync(loginDto.Email);
                if (user == null || !BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
                {
                    return Unauthorized("Invalid Credentials");
                }

                if (!user.IsApproved)
                {
                    return Unauthorized("Your account is pending admin approval. Please contact an administrator.");
                }

                var token = _jwtTokenService.GenerateToken(user);

                return Ok(new
                {
                    Token = token,
                    User = new
                    {
                        Id = user.Id,
                        Email = user.Email,
                        Role = user.Role.ToString().ToLower(),
                        Name = user.FullName,
                        PlantationName = user.PlantationName,
                        Latitude = user.Location?.Coordinates.Latitude,
                        Longitude = user.Location?.Coordinates.Longitude
                    }
                });
            }
            catch (TimeoutException)
            {
                return StatusCode(503, new { error = "Database connection timed out. Please check MongoDB Atlas network access settings." });
            }
            catch (MongoException ex)
            {
                return StatusCode(503, new { error = "Database error.", detail = ex.Message });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            try
            {
                // Check if email already exists
                if (await _userRepository.ExistsAsync(registerDto.Email))
                {
                    return BadRequest("Email already registered");
                }

                // Parse role
                if (!Enum.TryParse<UserRole>(registerDto.Role, ignoreCase: true, out var role))
                {
                    return BadRequest("Invalid role. Valid roles: Farmer, Admin, Researcher, Buyer, Exporter");
                }

                // Build GeoJSON location if coordinates provided
                GeoJsonPoint<GeoJson2DGeographicCoordinates>? location = null;
                if (registerDto.Latitude.HasValue && registerDto.Longitude.HasValue)
                {
                    location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
                        new GeoJson2DGeographicCoordinates(
                            registerDto.Longitude.Value,
                            registerDto.Latitude.Value));
                }

                // Create user with hashed password
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = registerDto.FullName,
                    Email = registerDto.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(registerDto.Password),
                    Role = role,
                    PlantationName = registerDto.PlantationName,
                    Location = location,
                    IsApproved = false,
                    CreatedAt = DateTime.UtcNow
                };

                await _userRepository.CreateUserAsync(user);

                // Generate token and return
                var token = _jwtTokenService.GenerateToken(user);

                return Ok(new
                {
                    Message = "Registration successful! Your account is pending admin approval.",
                    User = new
                    {
                        Id = user.Id,
                        Email = user.Email,
                        Role = user.Role.ToString().ToLower(),
                        Name = user.FullName
                    }
                });
            }
            catch (TimeoutException)
            {
                return StatusCode(503, new { error = "Database connection timed out. Please check MongoDB Atlas network access settings." });
            }
            catch (MongoException ex)
            {
                return StatusCode(503, new { error = "Database error.", detail = ex.Message });
            }
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdString, out var userId))
            {
                return Unauthorized();
            }

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                return NotFound("User not found");
            }

            return Ok(new
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role.ToString().ToLower(),
                Name = user.FullName,
                PlantationName = user.PlantationName,
                Latitude = user.Location?.Coordinates.Latitude,
                Longitude = user.Location?.Coordinates.Longitude,
                IsApproved = user.IsApproved
            });
        }

        [HttpPut("profile")]
        [Authorize]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdString, out var userId))
            {
                return Unauthorized();
            }

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return NotFound("User not found");

            // Update only provided fields
            if (!string.IsNullOrEmpty(dto.FullName))
                user.FullName = dto.FullName;

            if (!string.IsNullOrEmpty(dto.Password))
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            if (dto.PlantationName != null)
                user.PlantationName = dto.PlantationName;

            if (dto.Latitude.HasValue && dto.Longitude.HasValue)
            {
                user.Location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
                    new GeoJson2DGeographicCoordinates(
                        dto.Longitude.Value,
                        dto.Latitude.Value));
            }

            await _userRepository.UpdateAsync(user);

            return Ok(new
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role.ToString().ToLower(),
                Name = user.FullName,
                PlantationName = user.PlantationName,
                Latitude = user.Location?.Coordinates.Latitude,
                Longitude = user.Location?.Coordinates.Longitude
            });
        }
    }
}

