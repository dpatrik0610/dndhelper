using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Threading.Tasks;

namespace dndhelper.Core
{
    public class NotificationHub : Hub
    {
        private readonly ILogger _logger;

        public NotificationHub(ILogger logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            // Get userId from query string (passed from frontend)
            var httpContext = Context.GetHttpContext();
            var userId = httpContext?.Request.Query["userId"].ToString();

            if (!string.IsNullOrEmpty(userId))
            {
                // Add this connection to a group named after the userId
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
            var httpContext = Context.GetHttpContext();
            var userId = httpContext?.Request.Query["userId"].ToString();

            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.Information("❌ User ID: {UserId} disconnected", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}