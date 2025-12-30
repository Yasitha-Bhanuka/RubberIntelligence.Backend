using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Domain.Entities;
using RubberIntelligence.API.Domain.Enums;
using RubberIntelligence.API.Infrastructure.Security;

namespace RubberIntelligence.API.Controllers
{
    public class LoginDto
    {
        public required string Email { get; set; }
        public required string Password { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly JwtTokenService _jwtTokenService;

        public AuthController(JwtTokenService jwtTokenService)
        {
            _jwtTokenService = jwtTokenService;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginDto loginDto)
        {
            // Simulate Database User/Role Fetch
            User? user = null;

            if (loginDto.Email == "farmer@test.com" && loginDto.Password == "pass123")
            {
                user = new User 
                { 
                    Id = Guid.NewGuid(), 
                    Email = loginDto.Email, 
                    Role = UserRole.Farmer, 
                    FullName = "John Planter"
                };
            }
            else if (loginDto.Email == "admin@test.com" && loginDto.Password == "pass123")
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = loginDto.Email,
                    Role = UserRole.Admin,
                    FullName = "Admin User"
                };
            }

            if (user == null)
            {
                return Unauthorized("Invalid Credentials");
            }

            // Generate Token
            var token = _jwtTokenService.GenerateToken(user);

            return Ok(new
            {
                Token = token,
                User = new
                {
                    Email = user.Email,
                    Role = user.Role.ToString().ToLower(), // Lowercase to match frontend
                    Name = user.FullName
                }
            });
        }
    }
}
