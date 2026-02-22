using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver.GeoJsonObjectModel;
using RubberIntelligence.API.Data.Repositories;

namespace RubberIntelligence.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly IUserRepository _userRepository;

        public AdminController(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        /// <summary>
        /// Get all registered users (Admin only).
        /// </summary>
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _userRepository.GetAllUsersAsync();

            var result = users.Select(u => new
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                Role = u.Role.ToString(),
                PlantationName = u.PlantationName,
                IsApproved = u.IsApproved,
                CreatedAt = u.CreatedAt,
                Latitude = u.Location?.Coordinates.Latitude,
                Longitude = u.Location?.Coordinates.Longitude
            });

            return Ok(result);
        }

        /// <summary>
        /// Approve a user account.
        /// </summary>
        [HttpPut("users/{id}/approve")]
        public async Task<IActionResult> ApproveUser(Guid id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null) return NotFound("User not found");

            user.IsApproved = true;
            await _userRepository.UpdateAsync(user);

            return Ok(new { Message = $"User '{user.FullName}' has been approved.", IsApproved = true });
        }

        /// <summary>
        /// Reject / revoke approval for a user account.
        /// </summary>
        [HttpPut("users/{id}/reject")]
        public async Task<IActionResult> RejectUser(Guid id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null) return NotFound("User not found");

            user.IsApproved = false;
            await _userRepository.UpdateAsync(user);

            return Ok(new { Message = $"User '{user.FullName}' has been rejected.", IsApproved = false });
        }

        /// <summary>
        /// Delete a user account permanently.
        /// </summary>
        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null) return NotFound("User not found");

            await _userRepository.DeleteAsync(id);

            return Ok(new { Message = $"User '{user.FullName}' has been deleted." });
        }
    }
}
