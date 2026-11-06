using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace dndhelper.Models
{
    /// <summary>
    /// One page of the DM's campaign notebook. Private to the DMs unless shared; players get a stripped copy
    /// (no %%secret%% blocks, flags, pins or share list), built by ChronicleService.
    /// </summary>
    public class ChronicleEntry : IEntity, ICampaignScoped
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string? FolderId { get; set; }

        public ChronicleCategory Category { get; set; } = ChronicleCategory.Other;
        public List<string> Tags { get; set; } = new();

        public bool Pinned { get; set; }
        public ChronicleFlag Flag { get; set; } = ChronicleFlag.None;

        public ChronicleVisibility Visibility { get; set; } = ChronicleVisibility.Private;
        public List<string> SharedWith { get; set; } = new();

        public string? CreatedBy { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string? CreatedById { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }

        public bool IsVisibleTo(string? userId) => ChronicleSharing.Grants(Visibility, SharedWith, userId);
    }

    /// <summary>
    /// Sharing a folder shares everything under it: its entries and subfolders, including ones added later.
    /// Players never get the share list itself.
    /// </summary>
    public class ChronicleFolder : IEntity, ICampaignScoped
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }

        public string Name { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string? ParentId { get; set; }

        public ChronicleVisibility Visibility { get; set; } = ChronicleVisibility.Private;
        public List<string> SharedWith { get; set; } = new();

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }

        public bool IsVisibleTo(string? userId) => ChronicleSharing.Grants(Visibility, SharedWith, userId);
    }

    public static class ChronicleSharing
    {
        public static bool Grants(ChronicleVisibility visibility, List<string> sharedWith, string? userId) =>
            visibility == ChronicleVisibility.All
            || (visibility == ChronicleVisibility.Selected && userId != null && sharedWith.Contains(userId));
    }

    public class ChronicleComment : IEntity, ICampaignScoped
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CampaignId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string EntryId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool IsDm { get; set; }
        public string Text { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChronicleCategory { Location, Npc, Faction, Item, Lore, Quest, Session, Other }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChronicleFlag { None, Important, Todo, Idea, Done }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChronicleVisibility { Private, All, Selected }

    public class ChronicleEntryRequest
    {
        public string? CampaignId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string? FolderId { get; set; }
        public ChronicleCategory Category { get; set; } = ChronicleCategory.Other;
        public List<string>? Tags { get; set; }
        public bool Pinned { get; set; }
        public ChronicleFlag Flag { get; set; } = ChronicleFlag.None;
        public ChronicleVisibility Visibility { get; set; } = ChronicleVisibility.Private;
        public List<string>? SharedWith { get; set; }
    }

    public class ChronicleFolderRequest
    {
        public string? CampaignId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public ChronicleVisibility Visibility { get; set; } = ChronicleVisibility.Private;
        public List<string>? SharedWith { get; set; }
    }

    public class ChronicleCommentRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    public class ChronicleImageResult
    {
        public string Url { get; set; } = string.Empty;
    }
}
