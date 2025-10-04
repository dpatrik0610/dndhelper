using dndhelper.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace dndhelper.Authentication
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UserRole
    {
        User = 0,
        Guest = 1,
        Admin = 2,
        DungeonMaster = 3,
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UserStatus
    {
        Active = 0,
        Inactive = 1,
        Banned = 2,
        LogicDeleted = 3
    }

    public class User : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string? Email { get; set; } = string.Empty;
        [BsonRepresentation(BsonType.String)]
        public List<UserRole> Roles { get; set; } = new List<UserRole> { UserRole.User };
        public string? ProfilePictureUrl { get; set; } = null;
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime? LastLogin { get; set; }
        public List<string>? CharacterIds { get; set; } = new List<string>();
        public List<string>? CampaignIds { get; set; } = new List<string>();
        public UserStatus IsActive { get; set; } = UserStatus.Active;
        public Dictionary<string, string>? Settings { get; set; } = new Dictionary<string, string>();
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
    }
}
