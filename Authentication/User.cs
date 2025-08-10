using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace dndhelper.Authentication
{
    public enum UserRole
    {
        Player = 0,
        DungeonMaster = 1,
        Admin = 2,
        Guest = 3
    }

    public enum UserStatus
    {
        Active = 0,
        Inactive = 1,
        Banned = 2,
        LogicDeleted = 3
    }

    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string Email { get; set; } = null!;
        [BsonRepresentation(BsonType.String)]
        public UserRole Role { get; set; } = UserRole.Player;
        public string? ProfilePictureUrl { get; set; }
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime? LastLogin { get; set; }
        public List<string>? CharacterIds { get; set; }
        public List<string>? CampaignIds { get; set; }
        public UserStatus IsActive { get; set; } = UserStatus.Active;
        public Dictionary<string, string>? Settings { get; set; }
    }
}
