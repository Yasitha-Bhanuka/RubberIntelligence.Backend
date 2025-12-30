using Microsoft.AspNetCore.Mvc;
using RubberIntelligence.API.Data.Repositories;
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
        private readonly IUserRepository _userRepository;

        public AuthController(JwtTokenService jwtTokenService, IUserRepository userRepository)
        {
            _jwtTokenService = jwtTokenService;
            _userRepository = userRepository;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            // Fetch User from MongoDB
            var user = await _userRepository.GetByEmailAsync(loginDto.Email);

            // Validate User & Password (Plaintext for now as per previous logic)
            if (user == null || user.PasswordHash != loginDto.Password)
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
                    Role = user.Role.ToString().ToLower(), // "farmer", "admin"
                    Name = user.FullName
                }
            });
        }
    }
}
