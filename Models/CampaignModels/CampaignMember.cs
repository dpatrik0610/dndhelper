using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace dndhelper.Models.CampaignModels
{
    public class CampaignMember
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = null!;

        // Free-form so new campaign roles need no migration.
        public List<string> Roles { get; set; } = new();
    }

    public class CampaignMemberDto
    {
        public string UserId { get; set; } = null!;
        public string Username { get; set; } = null!;
        public List<string> Roles { get; set; } = new();
    }

    /// <summary>One row of the superadmin's all-campaigns overview.</summary>
    public class CampaignSummaryDto
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public System.DateTime? CreatedAt { get; set; }
        public List<string> Dms { get; set; } = new();
        public int MemberCount { get; set; }
        public int CharacterCount { get; set; }
        public bool AmIMember { get; set; }
    }

    public static class CampaignRoles
    {
        public const string Dm = "DM";
        public const string Player = "Player";
    }
}
