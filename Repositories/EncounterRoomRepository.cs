using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Models.EncounterRoomModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Utils;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class EncounterRoomRepository : IEncounterRoomRepository
    {
        private readonly IMongoCollection<EncounterRoom> _collection;
        private readonly ILogger _logger;

        public EncounterRoomRepository(ILogger logger, MongoDbContext context)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _collection = context.GetCollection<EncounterRoom>("EncounterRooms");

            // Ensure unique index on JoinCode
            var indexKeys = Builders<EncounterRoom>.IndexKeys.Ascending(r => r.JoinCode);
            var indexOptions = new CreateIndexOptions
            {
                Unique = true,
                Sparse = true
            };
            _collection.Indexes.CreateOne(new CreateIndexModel<EncounterRoom>(indexKeys, indexOptions));
        }

        // ──────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Builds a filter that matches a room by ID, expected revision, and not-deleted.
        /// Every mutation uses this to enforce optimistic concurrency.
        /// </summary>
        private static FilterDefinition<EncounterRoom> RevisionGuard(string roomId, int expectedRevision)
        {
            return Builders<EncounterRoom>.Filter.Eq(r => r.Id, roomId)
                 & Builders<EncounterRoom>.Filter.Eq(r => r.Revision, expectedRevision)
                 & Builders<EncounterRoom>.Filter.Eq(r => r.IsDeleted, false);
        }

        /// <summary>
        /// Appends Revision increment and UpdatedAt timestamp to any update definition.
        /// </summary>
        private static UpdateDefinition<EncounterRoom> WithRevisionBump(UpdateDefinition<EncounterRoom> update)
        {
            return update
                .Inc(r => r.Revision, 1)
                .Set(r => r.UpdatedAt, DateTime.UtcNow);
        }

        /// <summary>
        /// Executes a revision-guarded atomic update. Returns the new revision on success.
        /// Throws ConcurrencyException if the revision didn't match (another update happened first).
        /// </summary>
        private async Task<int> ExecuteRevisionedUpdateAsync(
            string roomId,
            int expectedRevision,
            UpdateDefinition<EncounterRoom> update,
            string operationName)
        {
            var result = await _collection.UpdateOneAsync(
                RevisionGuard(roomId, expectedRevision),
                WithRevisionBump(update));

            if (result.ModifiedCount == 0)
            {
                _logger.Warning(
                    "⚠️ Concurrency conflict on {Operation} for room {RoomId} at revision {Revision}",
                    operationName, roomId, expectedRevision);
                throw new ConcurrencyException();
            }

            var newRevision = expectedRevision + 1;
            _logger.Information(
                "✅ {Operation} succeeded for room {RoomId} → revision {Revision}",
                operationName, roomId, newRevision);

            return newRevision;
        }

        /// <summary>
        /// Generates a short, unique, human-friendly join code (6 uppercase alphanumeric characters).
        /// </summary>
        private static string GenerateJoinCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no O/0/1/I to avoid confusion
            var random = new Random();
            return new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray());
        }

        // ──────────────────────────────────────────────
        // Lifecycle
        // ──────────────────────────────────────────────

        public async Task<EncounterRoom> CreateAsync(EncounterRoom room)
        {
            room.CreatedAt = DateTime.UtcNow;
            room.UpdatedAt = DateTime.UtcNow;
            room.IsDeleted = false;
            room.Revision = 0;
            room.JoinCode = GenerateJoinCode();

            await _collection.InsertOneAsync(room);
            _logger.Information("🏠 Created EncounterRoom {RoomId} with JoinCode {JoinCode}", room.Id, room.JoinCode);
            return room;
        }

        public async Task<EncounterRoom?> GetByIdAsync(string roomId)
        {
            var filter = Builders<EncounterRoom>.Filter.Eq(r => r.Id, roomId)
                       & Builders<EncounterRoom>.Filter.Eq(r => r.IsDeleted, false);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<EncounterRoom?> GetByJoinCodeAsync(string joinCode)
        {
            var filter = Builders<EncounterRoom>.Filter.Eq(r => r.JoinCode, joinCode.ToUpperInvariant())
                       & Builders<EncounterRoom>.Filter.Eq(r => r.IsDeleted, false);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<EncounterRoom>> GetActiveRoomsByUserAsync(string userId)
        {
            var filter = Builders<EncounterRoom>.Filter.Eq(r => r.IsDeleted, false)
                       & (Builders<EncounterRoom>.Filter.Eq(r => r.DungeonMasterId, userId)
                        | Builders<EncounterRoom>.Filter.AnyEq(r => r.PlayerIds, userId));
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<bool> SoftDeleteAsync(string roomId)
        {
            var filter = Builders<EncounterRoom>.Filter.Eq(r => r.Id, roomId)
                       & Builders<EncounterRoom>.Filter.Eq(r => r.IsDeleted, false);

            var update = Builders<EncounterRoom>.Update
                .Set(r => r.IsDeleted, true)
                .Set(r => r.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            if (result.ModifiedCount > 0)
            {
                _logger.Information("🗑️ Soft-deleted EncounterRoom {RoomId}", roomId);
                return true;
            }

            return false;
        }

        // ──────────────────────────────────────────────
        // Players
        // ──────────────────────────────────────────────

        public Task<int> AddPlayerAsync(string roomId, int expectedRevision, string playerId)
        {
            var update = Builders<EncounterRoom>.Update.AddToSet(r => r.PlayerIds, playerId);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "AddPlayer");
        }

        public Task<int> RemovePlayerAsync(string roomId, int expectedRevision, string playerId)
        {
            var update = Builders<EncounterRoom>.Update.Pull(r => r.PlayerIds, playerId);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "RemovePlayer");
        }

        // ──────────────────────────────────────────────
        // Entities
        // ──────────────────────────────────────────────

        public Task<int> AddEntityAsync(string roomId, int expectedRevision, SessionEntity entity)
        {
            var update = Builders<EncounterRoom>.Update.Push(r => r.Entities, entity);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "AddEntity");
        }

        public async Task<int> UpdateEntityFieldsAsync(
            string roomId, int expectedRevision, string entityId, Dictionary<string, object> updates)
        {
            // Build targeted $set operations for each field on the matched array element
            var updateDefs = new List<UpdateDefinition<EncounterRoom>>();
            foreach (var kvp in updates)
            {
                // Fields that are direct properties on SessionEntity
                var fieldPath = kvp.Key switch
                {
                    "Name" or "name" => "Entities.$.Name",
                    "OwnerId" or "ownerId" => "Entities.$.OwnerId",
                    "IsPlayer" or "isPlayer" => "Entities.$.IsPlayer",
                    "IsVisible" or "isVisible" => "Entities.$.IsVisible",
                    "Initiative" or "initiative" => "Entities.$.Initiative",
                    "Color" or "color" => "Entities.$.Color",
                    // Everything else goes into Attributes
                    _ => $"Entities.$.Attributes.{kvp.Key}"
                };

                updateDefs.Add(Builders<EncounterRoom>.Update.Set(fieldPath, BsonValue.Create(kvp.Value)));
            }

            var combinedUpdate = Builders<EncounterRoom>.Update.Combine(updateDefs);

            // Match the specific entity within the array
            var filter = RevisionGuard(roomId, expectedRevision)
                       & Builders<EncounterRoom>.Filter.ElemMatch(r => r.Entities, e => e.Id == entityId);

            var result = await _collection.UpdateOneAsync(filter, WithRevisionBump(combinedUpdate));

            if (result.ModifiedCount == 0)
            {
                _logger.Warning(
                    "⚠️ Concurrency conflict or entity not found on UpdateEntityFields for room {RoomId}, entity {EntityId}",
                    roomId, entityId);
                throw new ConcurrencyException();
            }

            var newRevision = expectedRevision + 1;
            _logger.Information("✅ UpdateEntityFields succeeded for room {RoomId}, entity {EntityId} → revision {Revision}",
                roomId, entityId, newRevision);
            return newRevision;
        }

        public Task<int> RemoveEntityAsync(string roomId, int expectedRevision, string entityId)
        {
            var update = Builders<EncounterRoom>.Update.PullFilter(
                r => r.Entities, e => e.Id == entityId);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "RemoveEntity");
        }

        // ──────────────────────────────────────────────
        // Tokens
        // ──────────────────────────────────────────────

        public Task<int> AddTokenAsync(string roomId, int expectedRevision, RoomToken token)
        {
            var update = Builders<EncounterRoom>.Update.Push(r => r.Tokens, token);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "AddToken");
        }

        public async Task<int> MoveTokenAsync(string roomId, int expectedRevision, string tokenId, Point2D position)
        {
            var filter = RevisionGuard(roomId, expectedRevision)
                       & Builders<EncounterRoom>.Filter.ElemMatch(r => r.Tokens, t => t.Id == tokenId);

            var update = Builders<EncounterRoom>.Update
                .Set("Tokens.$.Position", position);

            var result = await _collection.UpdateOneAsync(filter, WithRevisionBump(update));

            if (result.ModifiedCount == 0)
            {
                _logger.Warning(
                    "⚠️ Concurrency conflict or token not found on MoveToken for room {RoomId}, token {TokenId}",
                    roomId, tokenId);
                throw new ConcurrencyException();
            }

            var newRevision = expectedRevision + 1;
            _logger.Information("✅ MoveToken succeeded for room {RoomId}, token {TokenId} → revision {Revision}",
                roomId, tokenId, newRevision);
            return newRevision;
        }

        public Task<int> RemoveTokenAsync(string roomId, int expectedRevision, string tokenId)
        {
            var update = Builders<EncounterRoom>.Update.PullFilter(
                r => r.Tokens, t => t.Id == tokenId);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "RemoveToken");
        }

        // ──────────────────────────────────────────────
        // Map elements
        // ──────────────────────────────────────────────

        public Task<int> AddMapElementAsync(string roomId, int expectedRevision, MapElement element)
        {
            var update = Builders<EncounterRoom>.Update.Push(r => r.MapElements, element);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "AddMapElement");
        }

        public Task<int> RemoveMapElementAsync(string roomId, int expectedRevision, string elementId)
        {
            var update = Builders<EncounterRoom>.Update.PullFilter(
                r => r.MapElements, e => e.Id == elementId);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "RemoveMapElement");
        }

        public Task<int> ClearMapElementsAsync(string roomId, int expectedRevision)
        {
            var update = Builders<EncounterRoom>.Update.Set(r => r.MapElements, new List<MapElement>());
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "ClearMapElements");
        }

        // ──────────────────────────────────────────────
        // Turn state
        // ──────────────────────────────────────────────

        public Task<int> UpdateTurnStateAsync(string roomId, int expectedRevision, TurnState turnState)
        {
            var update = Builders<EncounterRoom>.Update.Set(r => r.TurnState, turnState);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "UpdateTurnState");
        }

        // ──────────────────────────────────────────────
        // Map settings
        // ──────────────────────────────────────────────

        public Task<int> UpdateMapSettingsAsync(string roomId, int expectedRevision, MapSettings mapSettings)
        {
            var update = Builders<EncounterRoom>.Update.Set(r => r.MapSettings, mapSettings);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "UpdateMapSettings");
        }

        // ──────────────────────────────────────────────
        // Inventory
        // ──────────────────────────────────────────────

        public Task<int> AddInventoryAsync(string roomId, int expectedRevision, string inventoryId)
        {
            var update = Builders<EncounterRoom>.Update.AddToSet(r => r.InventoryIds, inventoryId);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "AddInventory");
        }

        public Task<int> RemoveInventoryAsync(string roomId, int expectedRevision, string inventoryId)
        {
            var update = Builders<EncounterRoom>.Update.Pull(r => r.InventoryIds, inventoryId);
            return ExecuteRevisionedUpdateAsync(roomId, expectedRevision, update, "RemoveInventory");
        }

        // ──────────────────────────────────────────────
        // Join code
        // ──────────────────────────────────────────────

        public async Task<string> RegenerateJoinCodeAsync(string roomId)
        {
            var newCode = GenerateJoinCode();

            var filter = Builders<EncounterRoom>.Filter.Eq(r => r.Id, roomId)
                       & Builders<EncounterRoom>.Filter.Eq(r => r.IsDeleted, false);

            var update = Builders<EncounterRoom>.Update
                .Set(r => r.JoinCode, newCode)
                .Set(r => r.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);

            if (result.ModifiedCount == 0)
                throw new InvalidOperationException($"Room not found: {roomId}");

            _logger.Information("🔑 Regenerated JoinCode for room {RoomId} → {JoinCode}", roomId, newCode);
            return newCode;
        }
    }
}
