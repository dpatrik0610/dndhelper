using dndhelper.Models.EncounterRoomModels;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    public class EncounterRoom : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string Name { get; set; } = string.Empty;

        // Access
        public string DungeonMasterId { get; set; } = string.Empty;
        public List<string> PlayerIds { get; set; } = new();
        public string JoinCode { get; set; } = string.Empty;

        // Inventory linking (references existing Inventory documents)
        [BsonRepresentation(BsonType.ObjectId)]
        public List<string> InventoryIds { get; set; } = new();

        // Embedded state
        public List<SessionEntity> Entities { get; set; } = new();
        public List<RoomToken> Tokens { get; set; } = new();
        public List<MapElement> MapElements { get; set; } = new();
        public TurnState TurnState { get; set; } = new();
        public MapSettings MapSettings { get; set; } = new();

        // Concurrency
        public int Revision { get; set; } = 0;

        // IEntity
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; }
    }
}
