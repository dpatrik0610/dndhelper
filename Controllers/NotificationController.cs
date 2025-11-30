using dndhelper.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Optional: require authentication
    public class NotificationController : ControllerBase
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger _logger;

        public NotificationController(IHubContext<NotificationHub> hubContext, ILogger logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Send notification to specific users by their userIds
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SendNotification([FromBody] NotificationRequest request)
        {
            var notificationData = new
            {
                id = Guid.NewGuid(),
                content = request.Content,
                sender = request.Sender,
                timestamp = DateTime.UtcNow
            };

            // Send to specific users
            if (request.UserIds != null && request.UserIds.Any())
            {
                _logger.Information("🎯 Sending notification to {Count} specific users", request.UserIds.Count);

                // Send to each user's group
                var tasks = request.UserIds.Select(userId =>
                {
                    _logger.Debug("  → Targeting group: user_{UserId}", userId);
                    return _hubContext.Clients.Group($"user_{userId}")
                        .SendAsync("ReceiveNotification", notificationData);
                });

                await Task.WhenAll(tasks);

                return Ok(new
                {
                    Message = "Notification sent to specific users",
                    UserCount = request.UserIds.Count,
                    UserIds = request.UserIds
                });
            }

            // Send to all users
            _logger.Information("📢 Broadcasting notification to ALL users");
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", notificationData);

            return Ok(new { Message = "Notification sent to all users" });
        }

        /// <summary>
        /// Send notification to all users except specific ones
        /// </summary>
        [HttpPost("except")]
        public async Task<IActionResult> SendNotificationExcept([FromBody] NotificationRequest request)
        {
            var notificationData = new
            {
                id = Guid.NewGuid(),
                content = request.Content,
                sender = request.Sender,
                timestamp = DateTime.UtcNow
            };

            if (request.UserIds != null && request.UserIds.Any())
            {
                _logger.Information("📢 Broadcasting notification to all EXCEPT {Count} users", request.UserIds.Count);

                // Get all group names to exclude
                var excludeGroups = request.UserIds.Select(id => $"user_{id}").ToList();

                await _hubContext.Clients.AllExcept(excludeGroups)
                    .SendAsync("ReceiveNotification", notificationData);

                return Ok(new
                {
                    Message = "Notification sent to all except specified users",
                    ExcludedUserCount = request.UserIds.Count
                });
            }

            _logger.Information("📢 Broadcasting notification to ALL users");
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", notificationData);
            return Ok(new { Message = "Notification sent to all users" });
        }
    }

    public class NotificationRequest
    {
        public string Content { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public List<string>? UserIds { get; set; }
    }
}