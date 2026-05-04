using dndhelper.Models.EncounterRoomModels;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Threading.Tasks;

namespace dndhelper.Core
{
    /// <summary>
    /// SignalR hub for real-time encounter room interactions.
    /// Thin transport layer — all logic lives in the service.
    /// 
    /// Groups: room_{roomId}
    /// Connection query params: userId
    /// </summary>
    public class EncounterRoomHub : Hub
    {
        private readonly IEncounterRoomService _roomService;
        private readonly ILogger _logger;

        public EncounterRoomHub(IEncounterRoomService roomService, ILogger logger)
        {
            _roomService = roomService;
            _logger = logger;
        }

        // ──────────────────────────────────────────────
        // Connection lifecycle
        // ──────────────────────────────────────────────

        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            var userId = httpContext?.Request.Query["userId"].ToString();

            if (!string.IsNullOrEmpty(userId))
            {
                // Also add to user-specific group for targeted messages (invites)
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.Information("✅ EncounterRoom hub: user {UserId} connected ({ConnectionId})",
                    userId, Context.ConnectionId);
            }
            else
            {
                _logger.Warning("⚠️ EncounterRoom hub: connection without userId ({ConnectionId})",
                    Context.ConnectionId);
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
                _logger.Information("❌ EncounterRoom hub: user {UserId} disconnected", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // ──────────────────────────────────────────────
        // Room lifecycle
        // ──────────────────────────────────────────────

        public async Task<object> JoinRoom(string joinCode)
        {
            var response = await _roomService.JoinRoomAsync(joinCode);

            // Add this connection to the room's SignalR group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{response.RoomId}");

            _logger.Information("🚪 Connection {ConnectionId} joined room group {RoomId}",
                Context.ConnectionId, response.RoomId);

            // Send full room state to the joining client
            await Clients.Caller.SendAsync("RoomStateSync", response.RoomState);

            return new { roomId = response.RoomId };
        }

        public async Task LeaveRoom(string roomId)
        {
            await _roomService.LeaveRoomAsync(roomId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room_{roomId}");

            _logger.Information("🚪 Connection {ConnectionId} left room group {RoomId}",
                Context.ConnectionId, roomId);
        }

        /// <summary>
        /// Joins a SignalR group for a room the user is already a member of (e.g. on reconnect).
        /// Does NOT add the user as a player — use JoinRoom with a join code for that.
        /// </summary>
        public async Task ReJoinRoom(string roomId)
        {
            // Verify the user is still a member
            var room = await _roomService.GetRoomAsync(roomId);
            if (room == null) return;

            await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{roomId}");
            await Clients.Caller.SendAsync("RoomStateSync", room);

            _logger.Information("🔄 Connection {ConnectionId} re-joined room group {RoomId}",
                Context.ConnectionId, roomId);
        }

        // ──────────────────────────────────────────────
        // Entity management
        // ──────────────────────────────────────────────

        public async Task AddEntity(RoomActionEnvelope<AddEntityRequest> envelope)
        {
            await _roomService.AddEntityAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action);
        }

        public async Task UpdateEntity(RoomActionEnvelope<UpdateEntityRequest> envelope)
        {
            await _roomService.UpdateEntityAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action);
        }

        public async Task RemoveEntity(RoomActionEnvelope<RemoveEntityRequest> envelope)
        {
            await _roomService.RemoveEntityAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action.EntityId);
        }

        // ──────────────────────────────────────────────
        // Token management
        // ──────────────────────────────────────────────

        public async Task AddToken(RoomActionEnvelope<AddTokenRequest> envelope)
        {
            await _roomService.AddTokenAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action);
        }

        public async Task MoveToken(RoomActionEnvelope<MoveTokenRequest> envelope)
        {
            await _roomService.MoveTokenAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action);
        }

        public async Task RemoveToken(RoomActionEnvelope<RemoveTokenRequest> envelope)
        {
            await _roomService.RemoveTokenAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action.TokenId);
        }

        // ──────────────────────────────────────────────
        // Map drawing
        // ──────────────────────────────────────────────

        public async Task AddMapElement(RoomActionEnvelope<AddMapElementRequest> envelope)
        {
            await _roomService.AddMapElementAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action);
        }

        public async Task RemoveMapElement(RoomActionEnvelope<RemoveMapElementRequest> envelope)
        {
            await _roomService.RemoveMapElementAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action.ElementId);
        }

        public async Task ClearMapElements(RoomActionEnvelope<object> envelope)
        {
            await _roomService.ClearMapElementsAsync(envelope.RoomId, envelope.ExpectedRevision);
        }

        // ──────────────────────────────────────────────
        // Turn management
        // ──────────────────────────────────────────────

        public async Task SetInitiative(RoomActionEnvelope<SetInitiativeRequest> envelope)
        {
            await _roomService.SetInitiativeAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action);
        }

        public async Task AdvanceTurn(RoomActionEnvelope<object> envelope)
        {
            await _roomService.AdvanceTurnAsync(envelope.RoomId, envelope.ExpectedRevision);
        }

        public async Task StartCombat(RoomActionEnvelope<object> envelope)
        {
            await _roomService.StartCombatAsync(envelope.RoomId, envelope.ExpectedRevision);
        }

        public async Task EndCombat(RoomActionEnvelope<object> envelope)
        {
            await _roomService.EndCombatAsync(envelope.RoomId, envelope.ExpectedRevision);
        }

        // ──────────────────────────────────────────────
        // Map settings
        // ──────────────────────────────────────────────

        public async Task UpdateMapSettings(RoomActionEnvelope<UpdateMapSettingsRequest> envelope)
        {
            await _roomService.UpdateMapSettingsAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action);
        }

        // ──────────────────────────────────────────────
        // Inventory
        // ──────────────────────────────────────────────

        public async Task AddInventory(RoomActionEnvelope<AddInventoryRequest> envelope)
        {
            await _roomService.AddInventoryAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action.InventoryId);
        }

        public async Task RemoveInventory(RoomActionEnvelope<RemoveInventoryRequest> envelope)
        {
            await _roomService.RemoveInventoryAsync(envelope.RoomId, envelope.ExpectedRevision, envelope.Action.InventoryId);
        }
    }
}
