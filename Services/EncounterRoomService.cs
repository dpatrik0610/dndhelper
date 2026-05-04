using dndhelper.Core;
using dndhelper.Models;
using dndhelper.Models.EncounterRoomModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using dndhelper.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class EncounterRoomService : IEncounterRoomService
    {
        private readonly IEncounterRoomRepository _repository;
        private readonly IHubContext<EncounterRoomHub> _hubContext;
        private readonly IEntitySyncService _entitySyncService;
        private readonly ILogger _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public EncounterRoomService(
            IEncounterRoomRepository repository,
            IHubContext<EncounterRoomHub> hubContext,
            IEntitySyncService entitySyncService,
            ILogger logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _repository = Guard.NotNull(repository, nameof(repository));
            _hubContext = Guard.NotNull(hubContext, nameof(hubContext));
            _entitySyncService = Guard.NotNull(entitySyncService, nameof(entitySyncService));
            _logger = Guard.NotNull(logger, nameof(logger));
            _httpContextAccessor = Guard.NotNull(httpContextAccessor, nameof(httpContextAccessor));
        }

        // ──────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────

        private string GetCurrentUserId()
        {
            var userId = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                throw new UnauthorizedAccessException("User is not authenticated.");
            return userId;
        }

        private async Task<EncounterRoom> GetRoomOrThrowAsync(string roomId)
        {
            var room = await _repository.GetByIdAsync(roomId);
            if (room == null)
                throw new NotFoundException($"Room not found: {roomId}");
            return room;
        }

        private void EnsureDM(EncounterRoom room, string userId)
        {
            if (room.DungeonMasterId != userId)
                throw new UnauthorizedAccessException("Only the Dungeon Master can perform this action.");
        }

        private void EnsureRoomMember(EncounterRoom room, string userId)
        {
            if (room.DungeonMasterId != userId && !room.PlayerIds.Contains(userId))
                throw new UnauthorizedAccessException("You are not a member of this room.");
        }

        private bool IsDM(EncounterRoom room, string userId) => room.DungeonMasterId == userId;

        private Task BroadcastToRoom(string roomId, string eventName, object data)
        {
            return _hubContext.Clients.Group($"room_{roomId}").SendAsync(eventName, data);
        }

        // ──────────────────────────────────────────────
        // Room lifecycle
        // ──────────────────────────────────────────────

        public async Task<EncounterRoom> CreateRoomAsync(string name, MapSettings? mapSettings)
        {
            Guard.NotNullOrWhiteSpace(name, nameof(name));

            var userId = GetCurrentUserId();

            var room = new EncounterRoom
            {
                Name = name,
                DungeonMasterId = userId,
                MapSettings = mapSettings ?? new MapSettings()
            };

            var created = await _repository.CreateAsync(room);
            _logger.Information("🏠 User {UserId} created room {RoomId} '{RoomName}'", userId, created.Id, name);
            return created;
        }

        public async Task<string> RegenerateJoinCodeAsync(string roomId)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            return await _repository.RegenerateJoinCodeAsync(roomId);
        }

        public async Task<JoinRoomResponse> JoinRoomAsync(string joinCode)
        {
            Guard.NotNullOrWhiteSpace(joinCode, nameof(joinCode));

            var userId = GetCurrentUserId();
            var room = await _repository.GetByJoinCodeAsync(joinCode.ToUpperInvariant());

            if (room == null)
                throw new NotFoundException("Invalid join code.");

            // Already a member? Just return the room state.
            if (room.DungeonMasterId == userId || room.PlayerIds.Contains(userId))
                return new JoinRoomResponse(room.Id!, room);

            // Add as a player
            var newRevision = await _repository.AddPlayerAsync(room.Id!, room.Revision, userId);

            // Broadcast to existing room members
            await BroadcastToRoom(room.Id!, "PlayerJoined", new { userId, revision = newRevision });

            // Re-fetch the room to return current state
            var updatedRoom = await GetRoomOrThrowAsync(room.Id!);
            _logger.Information("👤 User {UserId} joined room {RoomId}", userId, room.Id);
            return new JoinRoomResponse(updatedRoom.Id!, updatedRoom);
        }

        public async Task LeaveRoomAsync(string roomId)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureRoomMember(room, userId);

            if (IsDM(room, userId))
                throw new InvalidOperationException("The DM cannot leave the room. End it instead.");

            var newRevision = await _repository.RemovePlayerAsync(roomId, room.Revision, userId);
            await BroadcastToRoom(roomId, "PlayerLeft", new { userId, revision = newRevision });
            _logger.Information("👤 User {UserId} left room {RoomId}", userId, roomId);
        }

        public async Task<EncounterRoom?> GetRoomAsync(string roomId)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureRoomMember(room, userId);
            return room;
        }

        public async Task<List<EncounterRoom>> GetMyRoomsAsync()
        {
            var userId = GetCurrentUserId();
            return await _repository.GetActiveRoomsByUserAsync(userId);
        }

        public async Task EndRoomAsync(string roomId)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            await _repository.SoftDeleteAsync(roomId);
            await BroadcastToRoom(roomId, "RoomEnded", new { roomId });
            _logger.Information("🏁 Room {RoomId} ended by DM {UserId}", roomId, userId);
        }

        // ──────────────────────────────────────────────
        // Entity management
        // ──────────────────────────────────────────────

        public async Task<int> AddEntityAsync(string roomId, int expectedRevision, AddEntityRequest request)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            var entity = new SessionEntity
            {
                Name = request.Name,
                IsPlayer = request.IsPlayer,
                OwnerId = request.OwnerId,
                Color = request.Color ?? "#FFFFFF",
                Attributes = request.Attributes != null
                    ? new BsonDocument(request.Attributes.ToDictionary(k => k.Key, v => BsonValue.Create(v.Value)))
                    : new BsonDocument()
            };

            var newRevision = await _repository.AddEntityAsync(roomId, expectedRevision, entity);
            await BroadcastToRoom(roomId, "EntityAdded", new { entity, revision = newRevision });
            return newRevision;
        }

        public async Task<int> UpdateEntityAsync(string roomId, int expectedRevision, UpdateEntityRequest request)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureRoomMember(room, userId);

            // Players can only update entities they own
            if (!IsDM(room, userId))
            {
                var entity = room.Entities.FirstOrDefault(e => e.Id == request.EntityId);
                if (entity == null)
                    throw new NotFoundException($"Entity not found: {request.EntityId}");
                if (entity.OwnerId != userId)
                    throw new UnauthorizedAccessException("You can only update your own entities.");
            }

            var newRevision = await _repository.UpdateEntityFieldsAsync(
                roomId, expectedRevision, request.EntityId, request.Updates);

            await BroadcastToRoom(roomId, "EntityUpdated", new
            {
                entityId = request.EntityId,
                changes = request.Updates,
                revision = newRevision
            });

            return newRevision;
        }

        public async Task<int> RemoveEntityAsync(string roomId, int expectedRevision, string entityId)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            var newRevision = await _repository.RemoveEntityAsync(roomId, expectedRevision, entityId);
            await BroadcastToRoom(roomId, "EntityRemoved", new { entityId, revision = newRevision });
            return newRevision;
        }

        // ──────────────────────────────────────────────
        // Token management
        // ──────────────────────────────────────────────

        public async Task<int> AddTokenAsync(string roomId, int expectedRevision, AddTokenRequest request)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            var token = new RoomToken
            {
                EntityId = request.EntityId,
                Position = request.Position,
                Size = request.Size,
                ImageUrl = request.ImageUrl
            };

            var newRevision = await _repository.AddTokenAsync(roomId, expectedRevision, token);
            await BroadcastToRoom(roomId, "TokenAdded", new { token, revision = newRevision });
            return newRevision;
        }

        public async Task<int> MoveTokenAsync(string roomId, int expectedRevision, MoveTokenRequest request)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureRoomMember(room, userId);

            // Players can only move tokens for entities they own
            if (!IsDM(room, userId))
            {
                var token = room.Tokens.FirstOrDefault(t => t.Id == request.TokenId);
                if (token == null)
                    throw new NotFoundException($"Token not found: {request.TokenId}");

                var entity = room.Entities.FirstOrDefault(e => e.Id == token.EntityId);
                if (entity?.OwnerId != userId)
                    throw new UnauthorizedAccessException("You can only move your own tokens.");
            }

            var newRevision = await _repository.MoveTokenAsync(
                roomId, expectedRevision, request.TokenId, request.Position);

            await BroadcastToRoom(roomId, "TokenMoved", new
            {
                tokenId = request.TokenId,
                position = request.Position,
                revision = newRevision
            });

            return newRevision;
        }

        public async Task<int> RemoveTokenAsync(string roomId, int expectedRevision, string tokenId)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            var newRevision = await _repository.RemoveTokenAsync(roomId, expectedRevision, tokenId);
            await BroadcastToRoom(roomId, "TokenRemoved", new { tokenId, revision = newRevision });
            return newRevision;
        }

        // ──────────────────────────────────────────────
        // Map drawing
        // ──────────────────────────────────────────────

        public async Task<int> AddMapElementAsync(string roomId, int expectedRevision, AddMapElementRequest request)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureRoomMember(room, userId);

            var element = new MapElement
            {
                Type = request.Type,
                Shape = request.Shape,
                Points = request.Points,
                Color = request.Color,
                Thickness = request.Thickness,
                CreatedById = userId
            };

            var newRevision = await _repository.AddMapElementAsync(roomId, expectedRevision, element);
            await BroadcastToRoom(roomId, "MapElementAdded", new { element, revision = newRevision });
            return newRevision;
        }

        public async Task<int> RemoveMapElementAsync(string roomId, int expectedRevision, string elementId)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureRoomMember(room, userId);

            var newRevision = await _repository.RemoveMapElementAsync(roomId, expectedRevision, elementId);
            await BroadcastToRoom(roomId, "MapElementRemoved", new { elementId, revision = newRevision });
            return newRevision;
        }

        public async Task<int> ClearMapElementsAsync(string roomId, int expectedRevision)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            var newRevision = await _repository.ClearMapElementsAsync(roomId, expectedRevision);
            await BroadcastToRoom(roomId, "MapElementsCleared", new { revision = newRevision });
            return newRevision;
        }

        // ──────────────────────────────────────────────
        // Turn management
        // ──────────────────────────────────────────────

        public async Task<int> SetInitiativeAsync(string roomId, int expectedRevision, SetInitiativeRequest request)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureRoomMember(room, userId);

            // Players can only set initiative for their own entities
            if (!IsDM(room, userId))
            {
                var entity = room.Entities.FirstOrDefault(e => e.Id == request.EntityId);
                if (entity?.OwnerId != userId)
                    throw new UnauthorizedAccessException("You can only set initiative for your own entities.");
            }

            var updates = new Dictionary<string, object> { { "Initiative", request.Initiative } };
            var newRevision = await _repository.UpdateEntityFieldsAsync(
                roomId, expectedRevision, request.EntityId, updates);

            await BroadcastToRoom(roomId, "InitiativeSet", new
            {
                entityId = request.EntityId,
                initiative = request.Initiative,
                revision = newRevision
            });

            return newRevision;
        }

        public async Task<int> AdvanceTurnAsync(string roomId, int expectedRevision)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            if (!room.TurnState.IsActive)
                throw new InvalidOperationException("Combat is not active.");

            var sortedEntities = room.Entities
                .Where(e => e.Initiative.HasValue)
                .OrderByDescending(e => e.Initiative)
                .ToList();

            if (sortedEntities.Count == 0)
                throw new InvalidOperationException("No entities with initiative values.");

            var nextIndex = room.TurnState.CurrentIndex + 1;
            var nextRound = room.TurnState.Round;

            if (nextIndex >= sortedEntities.Count)
            {
                nextIndex = 0;
                nextRound++;
            }

            var newTurnState = new TurnState
            {
                Round = nextRound,
                CurrentIndex = nextIndex,
                IsActive = true
            };

            var newRevision = await _repository.UpdateTurnStateAsync(roomId, expectedRevision, newTurnState);
            await BroadcastToRoom(roomId, "TurnAdvanced", new { turnState = newTurnState, revision = newRevision });
            return newRevision;
        }

        public async Task<int> StartCombatAsync(string roomId, int expectedRevision)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            var newTurnState = new TurnState
            {
                Round = 1,
                CurrentIndex = 0,
                IsActive = true
            };

            var newRevision = await _repository.UpdateTurnStateAsync(roomId, expectedRevision, newTurnState);
            await BroadcastToRoom(roomId, "CombatStarted", new { turnState = newTurnState, revision = newRevision });
            return newRevision;
        }

        public async Task<int> EndCombatAsync(string roomId, int expectedRevision)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            var newTurnState = new TurnState
            {
                Round = 0,
                CurrentIndex = 0,
                IsActive = false
            };

            var newRevision = await _repository.UpdateTurnStateAsync(roomId, expectedRevision, newTurnState);
            await BroadcastToRoom(roomId, "CombatEnded", new { revision = newRevision });
            return newRevision;
        }

        // ──────────────────────────────────────────────
        // Map settings
        // ──────────────────────────────────────────────

        public async Task<int> UpdateMapSettingsAsync(string roomId, int expectedRevision, UpdateMapSettingsRequest request)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            var settings = room.MapSettings ?? new MapSettings();
            if (request.MapImageUrl != null) settings.MapImageUrl = request.MapImageUrl;
            if (request.GridType.HasValue) settings.GridType = request.GridType.Value;
            if (request.GridCellSize.HasValue) settings.GridCellSize = request.GridCellSize.Value;
            if (request.GridWidth.HasValue) settings.GridWidth = request.GridWidth.Value;
            if (request.GridHeight.HasValue) settings.GridHeight = request.GridHeight.Value;

            var newRevision = await _repository.UpdateMapSettingsAsync(roomId, expectedRevision, settings);
            await BroadcastToRoom(roomId, "MapSettingsUpdated", new { settings, revision = newRevision });
            return newRevision;
        }

        // ──────────────────────────────────────────────
        // Inventory
        // ──────────────────────────────────────────────

        public async Task<int> AddInventoryAsync(string roomId, int expectedRevision, string inventoryId)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            var newRevision = await _repository.AddInventoryAsync(roomId, expectedRevision, inventoryId);
            await BroadcastToRoom(roomId, "InventoryAdded", new { inventoryId, revision = newRevision });
            return newRevision;
        }

        public async Task<int> RemoveInventoryAsync(string roomId, int expectedRevision, string inventoryId)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            var newRevision = await _repository.RemoveInventoryAsync(roomId, expectedRevision, inventoryId);
            await BroadcastToRoom(roomId, "InventoryRemoved", new { inventoryId, revision = newRevision });
            return newRevision;
        }

        // ──────────────────────────────────────────────
        // Invites
        // ──────────────────────────────────────────────

        public async Task InvitePlayersAsync(string roomId, List<string> userIds)
        {
            var userId = GetCurrentUserId();
            var room = await GetRoomOrThrowAsync(roomId);
            EnsureDM(room, userId);

            // Send invite notification to each user via the existing notification hub
            await _entitySyncService.BroadcastToUsers(
                "RoomInvite",
                new
                {
                    roomId = room.Id,
                    roomName = room.Name,
                    joinCode = room.JoinCode,
                    invitedBy = userId
                },
                userIds);

            _logger.Information("📨 DM {UserId} invited {Count} players to room {RoomId}",
                userId, userIds.Count, roomId);
        }
    }
}
