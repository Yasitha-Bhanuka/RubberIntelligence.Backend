using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Services;

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
        private readonly TokenService _tokenService;

        public AuthController(TokenService tokenService)
        {
            _tokenService = tokenService;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginDto loginDto)
        {
            // Simulate Database User/Role Fetch
            string role = "";

            if (loginDto.Email == "farmer@test.com" && loginDto.Password == "pass123")
            {
                role = "grower";
            }
            else if (loginDto.Email == "admin@test.com" && loginDto.Password == "pass123")
            {
                role = "admin";
            }
            else
            {
                return Unauthorized("Invalid Credentials");
            }

            // Generate Token
            var token = _tokenService.CreateToken(loginDto.Email, role);

            return Ok(new
            {
                Token = token,
                User = new
                {
                    Email = loginDto.Email,
                    Role = role,
                    Name = role == "grower" ? "John Planter" : "Admin User"
                }
            });
        }
    }
}
