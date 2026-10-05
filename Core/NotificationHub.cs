using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Threading.Tasks;

namespace dndhelper.Core
{
    /// <summary>
    /// Each connection joins only its own user group, taken from the JWT (never from the query string).
    /// </summary>
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly ILogger _logger;

        public NotificationHub(ILogger logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;

            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.Information("✅ User ID: {UserId} connected with ConnectionId: {ConnectionId}", userId, Context.ConnectionId);
            }
            else
            {
                _logger.Warning("⚠️ Connection without userId: {ConnectionId}", Context.ConnectionId);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;

            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.Information("❌ User ID: {UserId} disconnected", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
