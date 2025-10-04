using dndhelper.Authentication;
using dndhelper.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // All endpoints require authentication by default
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ILogger _logger;

        public UserController(IUserService userService, ILogger logger)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // GET: api/user/me
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetSelf()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userService.GetSelfAsync(userId);
            if (user == null)
            {
                _logger.Warning($"User not found with ID: {userId}");
                return NotFound();
            }

            var response = new UserDataResponse(user);
            return Ok(response);
        }

        // GET: api/user
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<User>>> GetAll()
        {
            var users = await _userService.GetAllAsync();
            return Ok(users);
        }

        // GET: api/user/{id}
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<User>> GetById(string id)
        {
            var user = await _userService.GetByIdAsync(id);
            if (user == null) return NotFound();
            return Ok(user);
        }

        // PUT: api/user/{id}
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<User>> Update(string id, [FromBody] User user)
        {
            if (id != user.Id) return BadRequest("User ID mismatch");
            var updatedUser = await _userService.UpdateAsync(user);
            return Ok(updatedUser);
        }

        // PATCH: api/user/{id}/status
        [HttpPatch("{id}/status")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<User>> UpdateStatus(string id, [FromQuery] UserStatus status)
        {
            var updatedUser = await _userService.UpdateStatusAsync(id, status);
            return Ok(updatedUser);
        }

        // DELETE: api/user/{id}
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id)
        {
            var result = await _userService.DeleteAsync(id);
            if (!result) return NotFound();
            return NoContent();
        }

        // PATCH: api/user/{id}/logic-delete
        [HttpPatch("{id}/logic-delete")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> LogicDelete(string id)
        {
            var result = await _userService.LogicDeleteAsync(id);
            if (!result) return NotFound();
            return NoContent();
        }
        public class UserDataResponse
        {
            public string Username { get; set; }
            public string Email { get; set; }
            public List<UserRole> Roles { get; set; }
            public DateTime LastLogin { get; set; }

            // TODO: Characters, Campaigns, Settings, etc.

            public UserDataResponse(User user)
            {
                Username = user.Username;
                Email = user.Email ?? string.Empty;
                Roles = user.Roles;
                LastLogin = user.LastLogin ?? DateTime.MinValue;
            }
        }
    }
}
