using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    public class Quest : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public required string CampaignId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public List<string>? InvolvedCharacterIds { get; set; } = new();
        public required string Title { get; set; }
        public string? Description { get; set; }

        public string? Location { get; set; }
        public List<QuestObjective> Objectives { get; set; } = new();
        public QuestStatus Status { get; set; } = QuestStatus.Available;
        public QuestType Type { get; set; } = QuestType.Undefined;


        public List<string> RewardItemIds { get; set; } = new();
        public List<Currency> RewardCurrencies { get; set; } = new();

        // METADATA
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public enum QuestType { Main, Side, Faction, Personal, Undefined }

    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public enum QuestStatus { Available, Unavailable, Active, Completed, Failed }

    public class QuestObjective
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public string? Description { get; set; } = string.Empty;
        public bool IsCompleted { get; set; } = false;
        public int CurrentProgress { get; set; } = 0;
        public int CompletionThreshold { get; set; } = 1;
    }
}
