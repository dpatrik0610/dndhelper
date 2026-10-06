using dndhelper.Authentication;
using System;
using System.Collections.Generic;

namespace dndhelper.Models.DTOs
{
    /// <summary>One row of the superadmin's user manager. Never carries the password hash.</summary>
    public class AdminUserDto
    {
        public string Id { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string? Email { get; set; }
        public List<UserRole> Roles { get; set; } = new();
        public UserStatus Status { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? LastLogin { get; set; }
        public List<AdminUserCharacterDto> Characters { get; set; } = new();
        public List<AdminUserCampaignDto> Campaigns { get; set; } = new();
    }

    public class AdminUserCharacterDto
    {
        public string Id { get; set; } = null!;
        public string? Name { get; set; }
        public string? CharacterClass { get; set; }
        public int? Level { get; set; }
        public string? CampaignId { get; set; }
    }

    public class AdminUserCampaignDto
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public List<string> Roles { get; set; } = new();
    }

    /// <summary>Create (Username and Password required) or patch (only the sent fields change).</summary>
    public class AdminUserRequest
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Email { get; set; }
        public List<UserRole>? Roles { get; set; }
        public UserStatus? Status { get; set; }
    }
}
