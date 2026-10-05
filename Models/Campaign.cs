using dndhelper.Authorization;
using dndhelper.Models.CampaignModels;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace dndhelper.Models
{
    public class Campaign : IEntity, IOwnedResource
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        // Ownership and participants
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string> CharacterIds { get; set; } = new();

        // Source of truth for who is in the campaign and with which roles.
        public List<CampaignMember> Members { get; set; } = new();

        // Mirror of the DM members, kept for existing OwnerIds-based checks. Write via SyncOwners().
        public List<string>? OwnerIds { get; set; } = new List<string>();

        public string? InviteCode { get; set; }

        // Core content types (see CoreContentTypes) this campaign has imported.
        public List<string> CoreImports { get; set; } = new();

        // Related entities
        public List<string> WorldIds { get; set; } = new();
        public List<string> QuestIds { get; set; } = new();
        public List<string> NoteIds { get; set; } = new();

        // Metadata
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsActive { get; set; } = true;

        // Session tracking (for later use)
        public string? CurrentSessionId { get; set; }
        public List<string> SessionIds { get; set; } = new();

        [BsonRepresentation(BsonType.ObjectId)]
        public string? ActiveEncounterId { get; set; }

        public CampaignMember? GetMember(string? userId) =>
            userId == null ? null : Members?.FirstOrDefault(m => m.UserId == userId);

        public bool IsMember(string? userId) => GetMember(userId) != null;

        public bool HasRole(string? userId, string role) => GetMember(userId)?.Roles.Contains(role) == true;

        public bool IsDm(string? userId) => HasRole(userId, CampaignRoles.Dm);

        public List<string> DmIds() =>
            (Members ?? new()).Where(m => m.Roles.Contains(CampaignRoles.Dm)).Select(m => m.UserId).ToList();

        public void AddMember(string userId, string role)
        {
            Members ??= new();
            var member = GetMember(userId);
            if (member == null)
                Members.Add(new CampaignMember { UserId = userId, Roles = new() { role } });
            else if (!member.Roles.Contains(role))
                member.Roles.Add(role);
            SyncOwners();
        }

        public void SyncOwners() => OwnerIds = DmIds();
    }
}
