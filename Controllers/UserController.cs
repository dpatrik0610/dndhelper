using dndhelper.Authentication;
using dndhelper.Core;
using dndhelper.Models.DTOs;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ILogger _logger;
        private readonly IHubContext<NotificationHub> _hub;

        public UserController(IUserService userService, ILogger logger, IHubContext<NotificationHub> hub)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
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
                _logger.Warning("User not found with ID: {UserId}", userId);
                return NotFound();
            }

            var response = new UserDataResponse(user);
            return Ok(response);
        }

        [HttpGet("me/settings")]
        [Authorize]
        public async Task<ActionResult<Dictionary<string, string>>> GetUserSettings()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userService.GetSelfAsync(userId);
            if (user == null)
            {
                _logger.Warning("User not found with ID: {UserId}", userId);
                return NotFound();
            }

            Dictionary<string, string> response = user?.Settings ?? new Dictionary<string, string>();

            return Ok(response);
        }


        [HttpPut("me/settings")]
        [Authorize]
        public async Task<ActionResult<Dictionary<string, string>>> UpdateUserSettings([FromBody] Dictionary<string, string> settings)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userService.GetSelfAsync(userId);
            if (user == null)
            {
                _logger.Warning("User not found with ID: {UserId}", userId);
                return NotFound();
            }

            var updated = await _userService.UpdateSettingsForUser(user, settings);
            Dictionary<string, string> response = updated?.Settings ?? new Dictionary<string, string>();

            return Ok(response);
        }

        // POST: api/user/me/critters/{critter} (a theme critter was caught; returns the user's new score row)
        [HttpPost("me/critters/{critter}")]
        [EnableRateLimiting(AuthRateLimits.CritterCatch)]
        public async Task<ActionResult<CritterScoreDto>> CatchCritter(string critter)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();
            var score = await _userService.RecordCritterCatchAsync(userId, critter);
            // Every signed-in client is on this hub: open scoreboards update live.
            await _hub.Clients.All.SendAsync("CritterScoreChanged", score);
            return Ok(score);
        }

        // GET: api/user/critters/scoreboard (everyone's catches, most first)
        [HttpGet("critters/scoreboard")]
        public async Task<ActionResult<List<CritterScoreDto>>> CritterScoreboard() =>
            Ok(await _userService.GetCritterScoreboardAsync());

        // GET: api/user (superadmin user manager: every user with characters and campaigns)
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<AdminUserDto>>> GetAll() =>
            Ok(await _userService.GetAdminOverviewAsync());

        // POST: api/user
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<AdminUserDto>> Create([FromBody] AdminUserRequest request) =>
            Ok(await _userService.AdminCreateAsync(request));

        // PATCH: api/user/{id} (only the sent fields change; a password resets it)
        [HttpPatch("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<AdminUserDto>> Update(string id, [FromBody] AdminUserRequest request) =>
            Ok(await _userService.AdminUpdateAsync(id, request));

        // DELETE: api/user/{id}
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id)
        {
            var result = await _userService.AdminDeleteAsync(id);
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
            public DateTime DateCreated { get; set; }

            public Dictionary<string, string> Settings { get; set; }

            public UserDataResponse(User user)
            {
                Username = user.Username;
                Email = user.Email ?? string.Empty;
                Roles = user.Roles;
                LastLogin = user.LastLogin ?? DateTime.MinValue;
                DateCreated = user.DateCreated;
                Settings = user.Settings ?? new Dictionary<string, string>();
            }
        }
    }
}
