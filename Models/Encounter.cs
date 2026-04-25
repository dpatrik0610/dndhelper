using dndhelper.Authorization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace dndhelper.Models
{
    public class Encounter : IEntity, IOwnedResource
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string CampaignId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string? SessionId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public List<string> OwnerIds { get; set; } = new();

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? MapUrl { get; set; }
        public List<string> ImageUrls { get; set; } = new();
        public string? DmNote { get; set; }
        public string? Location { get; set; }

        public List<EncounterEntity> Entities { get; set; } = new();
        public List<EncounterLootItem> Loot { get; set; } = new();

        public EncounterStatus Status { get; set; } = EncounterStatus.Planned;

        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; }
    }

    public class EncounterEntity
    {
        public EncounterEntityType Type { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? ReferenceId { get; set; }

        public string Name { get; set; } = string.Empty;
        public int? Initiative { get; set; }
        public int Quantity { get; set; } = 1;
        public string? Note { get; set; }
        public EncounterEntityStatus Status { get; set; } = EncounterEntityStatus.Alive;
    }

    public class EncounterLootItem
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string? EquipmentId { get; set; }

        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public string? Note { get; set; }
        public bool IsClaimed { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EncounterStatus
    {
        Planned,
        Active,
        Completed,
        Cancelled
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EncounterEntityType
    {
        PlayerCharacter,
        Enemy,
        Npc,
        Ally,
        Other
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EncounterEntityStatus
    {
        Alive,
        Dead,
        Unconscious,
        Fled,
        Removed,
        Other
    }
}
