using dndhelper.Authentication;
using dndhelper.Authentication.Interfaces;
using dndhelper.Authorization;
using dndhelper.Models;
using dndhelper.Models.CampaignModels;
using dndhelper.Models.CharacterModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class CampaignService : BaseService<Campaign, ICampaignRepository>, ICampaignService
    {
        private readonly IUserRepository _userRepository;
        private readonly ICharacterRepository _characterRepository;
        private readonly IEncounterRepository _encounterRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly IEntitySyncService _entitySyncService;
        private readonly IAuthService _authService;

        public CampaignService(
            ICampaignRepository repository, 
            ILogger logger, 
            IUserRepository userRepository, 
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            ICharacterRepository characterRepository,
            IEncounterRepository encounterRepository,
            IInventoryRepository inventoryRepository,
            IEntitySyncService entitySyncService,
            IAuthService authService) : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _userRepository = Guard.NotNull(userRepository, nameof(userRepository));
            _characterRepository = Guard.NotNull(characterRepository, nameof(characterRepository));
            _encounterRepository = Guard.NotNull(encounterRepository, nameof(encounterRepository));
            _inventoryRepository = Guard.NotNull(inventoryRepository, nameof(inventoryRepository));
            _entitySyncService = Guard.NotNull(entitySyncService, nameof(entitySyncService));
            _authService = Guard.NotNull(authService, nameof(authService));
        }

        public async Task<Campaign> CreateAsync(Campaign campaign, string userId)
        {
            if (campaign == null)
                throw new ArgumentNullException(nameof(campaign));

            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                throw CustomExceptions.ThrowCustomException(_logger, $"User not found with ID: {userId}");

            campaign.CreatedAt = DateTime.UtcNow;
            // The creator is the campaign's DM; membership never comes from the client.
            campaign.Members = new List<CampaignMember>();
            campaign.AddMember(userId, CampaignRoles.Dm);
            campaign.InviteCode = NewInviteCode();
            campaign.CoreImports ??= new List<string>();
            campaign.CoreImports = campaign.CoreImports.Intersect(CoreContentTypes.All).ToList();

            _logger.Debug("Creating entity of type {EntityType}", typeof(Campaign).Name);

            var createdCampaign = await _repository.CreateAsync(campaign);
            if (createdCampaign == null || string.IsNullOrEmpty(createdCampaign.Id))
                throw CustomExceptions.ThrowCustomException(_logger, "Failed to create new campaign.");

            // Add campaign reference to user
            user.CampaignIds ??= new List<string>();
            user.CampaignIds.Add(createdCampaign.Id);
            await _userRepository.UpdateAsync(user);

            return createdCampaign;
        }

        public async Task<bool> DeleteAsync(string campaignId, string userId)
        {
            if (string.IsNullOrEmpty(campaignId))
                throw new ArgumentException("Campaign ID cannot be null or empty.", nameof(campaignId));

            if (string.IsNullOrEmpty(userId))
                throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                throw CustomExceptions.ThrowCustomException(_logger, $"User not found with ID: {userId}");

            var campaign = await Access.EnsureDmAsync(campaignId);

            // Logical delete
            var deleted = await _repository.LogicDeleteAsync(campaignId);
            if (!deleted)
                throw CustomExceptions.ThrowCustomException(_logger, $"Failed to logically delete campaign: {campaignId}");

            // Remove from user reference
            user.CampaignIds?.Remove(campaignId);
            await _userRepository.UpdateAsync(user);

            _logger.Information("User {UserId} deleted campaign {CampaignId}", userId, campaignId);

            return true;
        }

        public async Task<List<string>> GetCampaignDMIdsAsync(string campaignId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            var dmIds = campaign?.DmIds() ?? new List<string>();
            if (dmIds.Count == 0)
                return dmIds;

            var users = await _userRepository.GetByIdsAsync(dmIds);
            return users.Select(x => x.Id).ToList();
        }

        // ------------------------
        // ACCESS: members read, DMs write. The superadmin sees every campaign.
        // ------------------------
        public override async Task<Campaign?> GetByIdAsync(string id)
        {
            var campaign = await _repository.GetByIdAsync(id);
            if (campaign != null)
                await Access.EnsureMemberAsync(id);
            return campaign;
        }

        /// <summary>Campaigns I'm a member of. The superadmin too: everything else is in GetOverviewOfAllAsync.</summary>
        public override async Task<IEnumerable<Campaign>> GetAllAsync() =>
            await _repository.GetForMemberAsync(Access.UserId ?? string.Empty);

        /// <summary>Superadmin overview of every campaign on the site.</summary>
        public async Task<List<CampaignSummaryDto>> GetOverviewOfAllAsync()
        {
            if (!Access.IsSuperAdmin)
                throw new ForbiddenException("Only the superadmin can list every campaign.");

            var campaigns = (await _repository.GetAllAsync()).ToList();
            var dmIds = campaigns.SelectMany(c => c.DmIds()).Distinct().ToList();
            var names = (await _userRepository.GetByIdsAsync(dmIds)).ToDictionary(u => u.Id, u => u.Username);

            return campaigns
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new CampaignSummaryDto
                {
                    Id = c.Id!,
                    Name = c.Name,
                    Description = c.Description,
                    IsActive = c.IsActive,
                    CreatedAt = c.CreatedAt,
                    Dms = c.DmIds().Select(id => names.GetValueOrDefault(id) ?? "(deleted user)").ToList(),
                    MemberCount = c.Members?.Count ?? 0,
                    CharacterCount = c.CharacterIds?.Count ?? 0,
                    AmIMember = c.IsMember(Access.UserId),
                })
                .ToList();
        }

        public override async Task<Campaign?> UpdateAsync(Campaign campaign)
        {
            var existing = await Access.EnsureDmAsync(campaign.Id);

            // Membership, invite code and core imports have their own endpoints.
            campaign.Members = existing.Members;
            campaign.OwnerIds = existing.OwnerIds;
            campaign.InviteCode = existing.InviteCode;
            campaign.CoreImports = existing.CoreImports;
            campaign.UpdatedAt = DateTime.UtcNow;
            return await _repository.UpdateAsync(campaign);
        }

        // ------------------------
        // MEMBERSHIP
        // ------------------------
        public async Task<Campaign> JoinAsync(string inviteCode)
        {
            Guard.NotNullOrWhiteSpace(inviteCode, nameof(inviteCode));
            var userId = Access.UserId ?? throw new UnauthorizedAccessException("Not logged in.");

            var campaign = await _repository.GetByInviteCodeAsync(inviteCode.Trim().ToUpperInvariant())
                ?? throw new NotFoundException("No campaign with that invite code.");

            if (campaign.IsMember(userId))
                return campaign;

            campaign.AddMember(userId, CampaignRoles.Player);
            return await SaveMembershipAsync(campaign);
        }

        public async Task<List<CampaignMemberDto>> GetMembersAsync(string campaignId)
        {
            var campaign = await Access.EnsureMemberAsync(campaignId);
            var users = await _userRepository.GetByIdsAsync(campaign.Members.Select(m => m.UserId));
            var names = users.ToDictionary(u => u.Id, u => u.Username);

            return campaign.Members.Select(m => new CampaignMemberDto
            {
                UserId = m.UserId,
                Username = names.GetValueOrDefault(m.UserId) ?? "(deleted user)",
                Roles = m.Roles
            }).ToList();
        }

        public async Task<Campaign> SetMemberRolesAsync(string campaignId, string userId, List<string> roles)
        {
            var campaign = await Access.EnsureDmAsync(campaignId);
            var member = campaign.GetMember(userId) ?? throw new NotFoundException("User is not a member of this campaign.");

            roles = roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();
            if (roles.Count == 0)
                throw new ArgumentException("A member needs at least one role.");

            var wasDm = member.Roles.Contains(CampaignRoles.Dm);
            if (wasDm && !roles.Contains(CampaignRoles.Dm) && campaign.DmIds().Count == 1)
                throw new ArgumentException("A campaign needs at least one DM.");

            member.Roles = roles;
            campaign.SyncOwners();
            return await SaveMembershipAsync(campaign);
        }

        /// <summary>DMs remove anyone; members may remove themselves (leave).</summary>
        public async Task<Campaign> RemoveMemberAsync(string campaignId, string userId)
        {
            var campaign = userId == Access.UserId
                ? await Access.EnsureMemberAsync(campaignId)
                : await Access.EnsureDmAsync(campaignId);

            var member = campaign.GetMember(userId) ?? throw new NotFoundException("User is not a member of this campaign.");
            if (member.Roles.Contains(CampaignRoles.Dm) && campaign.DmIds().Count == 1)
                throw new ArgumentException("The last DM can't leave. Make someone else DM first.");

            // Their characters leave with them: kept by them, out of this campaign. A character shared with
            // other players stays, without them as an owner.
            var characters = await _characterRepository.GetByIdsAsync(campaign.CharacterIds ?? new List<string>());
            foreach (var character in characters.Where(c => c.CampaignId == campaignId && c.OwnerIds?.Contains(userId) == true))
            {
                if (character.OwnerIds!.Count == 1)
                {
                    await MoveCharacterAsync(character, null);
                    continue;
                }
                character.OwnerIds.Remove(userId);
                await _characterRepository.UpdateAsync(character);
                await _userRepository.RemoveCharacterIdAsync(userId, character.Id!);
                await SyncInventoryOwnersAsync(character.Id!);
            }

            campaign = (await _repository.GetByIdAsync(campaignId))!;
            member = campaign.GetMember(userId)!;
            campaign.Members.Remove(member);
            campaign.SyncOwners();
            return await SaveMembershipAsync(campaign);
        }

        public async Task<Campaign> RegenerateInviteCodeAsync(string campaignId)
        {
            var campaign = await Access.EnsureDmAsync(campaignId);
            campaign.InviteCode = NewInviteCode();
            return await SaveMembershipAsync(campaign);
        }

        public async Task<Campaign> SetCoreImportsAsync(string campaignId, List<string> types)
        {
            var campaign = await Access.EnsureDmAsync(campaignId);
            campaign.CoreImports = (types ?? new()).Intersect(CoreContentTypes.All).ToList();
            return await SaveMembershipAsync(campaign);
        }

        // UpdateAsync skips nulls but writes lists whole, so these fields round-trip as set here.
        private async Task<Campaign> SaveMembershipAsync(Campaign campaign)
        {
            campaign.UpdatedAt = DateTime.UtcNow;
            return await _repository.UpdateAsync(campaign)
                ?? throw new InvalidOperationException("Failed to save campaign.");
        }

        private static string NewInviteCode() =>
            RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 8);

        // ------------------------
        // PLAYER MANAGEMENT
        // ------------------------
        public async Task<List<Character>> GetCharactersAsync(string campaignId)
        {
            List<Character> characters = new List<Character>();
            var campaign = await _repository.GetByIdAsync(campaignId);

            if (campaign == null || campaign.CharacterIds.IsNullOrEmpty())
                return characters;

            characters = await _characterRepository.GetByIdsAsync(campaign.CharacterIds);
            return characters;
        }

        /// <summary>
        /// Moves a character into a campaign, or out of every campaign (null). Its own inventories travel with it
        /// (shared stashes with other characters stay put) and the old campaign stops listing it.
        /// </summary>
        private async Task MoveCharacterAsync(Character character, string? toCampaignId)
        {
            var fromCampaignId = character.CampaignId;
            if (fromCampaignId == toCampaignId) return;

            await _characterRepository.SetCampaignAsync(character.Id!, toCampaignId);
            foreach (var inventory in await _inventoryRepository.GetByCharacterIdAsync(character.Id!))
                if ((inventory.CharacterIds ?? new List<string>()).All(id => id == character.Id))
                    await _inventoryRepository.SetCampaignAsync(inventory.Id!, toCampaignId);
            character.CampaignId = toCampaignId;

            if (fromCampaignId != null && await _repository.GetByIdAsync(fromCampaignId) is { } from
                && from.CharacterIds.Remove(character.Id!))
                await _repository.UpdateAsync(from);
        }

        /// <summary>Recomputes inventory owners after a character's owners changed: an inventory belongs to the owners of all its characters.</summary>
        private async Task SyncInventoryOwnersAsync(string characterId)
        {
            foreach (var inventory in await _inventoryRepository.GetByCharacterIdAsync(characterId))
            {
                var characters = await _characterRepository.GetByIdsAsync(inventory.CharacterIds ?? new List<string>());
                inventory.OwnerIds = characters.SelectMany(c => c.OwnerIds ?? new List<string>()).Distinct().ToList();
                await _inventoryRepository.UpdateAsync(inventory);
            }
        }

        /// <summary>
        /// POST campaign/{id}/characters/{characterId}: the DM adds any character; a member may bring in a
        /// character they own that isn't in any campaign.
        /// </summary>
        public async Task<Campaign?> AddCharacterAsCallerAsync(string campaignId, string characterId)
        {
            if (!await Access.IsDmAsync(campaignId))
            {
                await Access.EnsureMemberAsync(campaignId);
                var character = await _characterRepository.GetByIdAsync(characterId)
                    ?? throw new NotFoundException("Character not found.");
                if (character.OwnerIds?.Contains(Access.UserId!) != true)
                    throw new ForbiddenException("You can only bring in your own characters.");
                if (character.CampaignId != null)
                    throw new ArgumentException("That character is still in another campaign.");
            }
            return await AddCharacterAsync(campaignId, characterId);
        }

        public async Task<Campaign?> AddCharacterAsync(string campaignId, string characterId)
        {
            if (await _repository.GetByIdAsync(campaignId) == null) return null;

            var character = await _characterRepository.GetByIdAsync(characterId)
                ?? throw new NotFoundException("Character not found.");

            // Keep both sides of the link in sync, and make the character's owners players.
            await MoveCharacterAsync(character, campaignId);
            var campaign = (await _repository.GetByIdAsync(campaignId))!;

            if (!campaign.CharacterIds.Contains(characterId))
                campaign.CharacterIds.Add(characterId);
            foreach (var ownerId in character.OwnerIds ?? new List<string>())
                if (!campaign.IsMember(ownerId))
                    campaign.AddMember(ownerId, CampaignRoles.Player);

            return await _repository.UpdateAsync(campaign);
        }

        /// <summary>
        /// DM hands a character to one or more campaign members (e.g. a character the DM built for a player).
        /// Keeps the owners' character lists and the character's inventories in step.
        /// </summary>
        public async Task<Character> SetCharacterOwnersAsync(string campaignId, string characterId, List<string> ownerIds)
        {
            var campaign = await Access.EnsureDmAsync(campaignId);
            var character = await _characterRepository.GetByIdAsync(characterId)
                ?? throw new NotFoundException("Character not found.");
            if (character.CampaignId != campaignId)
                throw new ArgumentException("That character isn't in this campaign.");

            ownerIds = (ownerIds ?? new()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            if (ownerIds.Count == 0)
                throw new ArgumentException("A character needs at least one player.");
            var outsider = ownerIds.FirstOrDefault(id => !campaign.IsMember(id));
            if (outsider != null)
                throw new ArgumentException("Only members of this campaign can be given a character.");

            var previous = character.OwnerIds ?? new List<string>();
            character.OwnerIds = ownerIds;
            character = await _characterRepository.UpdateAsync(character)
                ?? throw new InvalidOperationException("Failed to save character.");

            foreach (var added in ownerIds.Except(previous))
                await _userRepository.AddCharacterIdAsync(added, characterId);
            foreach (var removed in previous.Except(ownerIds))
                await _userRepository.RemoveCharacterIdAsync(removed, characterId);

            await SyncInventoryOwnersAsync(characterId);

            if (!campaign.CharacterIds.Contains(characterId))
                await AddCharacterAsync(campaignId, characterId);

            await BroadcastOwnersChangedAsync(campaign, character, previous);
            return character;
        }

        /// <summary>
        /// Live update: new players get "assigned" (the character appears in their list), dropped players
        /// get "unassigned" (it disappears), everyone else who can see it gets a plain "updated".
        /// </summary>
        private async Task BroadcastOwnersChangedAsync(Campaign campaign, Character character, List<string> previousOwners)
        {
            var user = await _authService.GetUserFromTokenAsync();
            var owners = character.OwnerIds ?? new List<string>();
            var dms = campaign.DmIds();

            var groups = new[]
            {
                ("assigned", owners.Except(previousOwners)),
                ("unassigned", previousOwners.Except(owners).Except(dms)),
                ("updated", owners.Intersect(previousOwners).Union(dms)),
            };

            foreach (var (action, recipients) in groups)
            {
                var ids = recipients.Distinct().ToList();
                if (ids.Count == 0) continue;

                await _entitySyncService.BroadcastToUsers(
                    "EntityChanged",
                    new
                    {
                        entityType = "Character",
                        entityId = character.Id,
                        action,
                        data = character,
                        changedBy = user.Username,
                        timestamp = DateTime.UtcNow,
                    },
                    ids,
                    excludeUserId: user.Id);
            }
        }

        public async Task<Campaign?> RemoveCharacterAsync(string campaignId, string characterId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null) return null;

            // The owner keeps the character; it (and its own inventories) just leave the campaign.
            var character = await _characterRepository.GetByIdAsync(characterId);
            if (character?.CampaignId == campaignId)
                await MoveCharacterAsync(character, null);

            campaign = (await _repository.GetByIdAsync(campaignId))!;
            if (campaign.CharacterIds.Remove(characterId))
                await _repository.UpdateAsync(campaign);
            return campaign;
        }

        // ------------------------
        // WORLD MANAGEMENT
        // ------------------------
        public async Task<Campaign?> AddWorldAsync(string campaignId, string worldId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null || campaign.WorldIds.Contains(worldId)) return campaign;

            campaign.WorldIds.Add(worldId);
            return await _repository.UpdateAsync(campaign);
        }

        public async Task<Campaign?> RemoveWorldAsync(string campaignId, string worldId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null) return null;

            campaign.WorldIds.Remove(worldId);
            return await _repository.UpdateAsync(campaign);
        }

        // ------------------------
        // QUEST MANAGEMENT
        // ------------------------
        public async Task<Campaign?> AddQuestAsync(string campaignId, string questId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null || campaign.QuestIds.Contains(questId)) return campaign;

            campaign.QuestIds.Add(questId);
            return await _repository.UpdateAsync(campaign);
        }

        public async Task<Campaign?> RemoveQuestAsync(string campaignId, string questId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null) return null;

            campaign.QuestIds.Remove(questId);
            return await _repository.UpdateAsync(campaign);
        }

        // ------------------------
        // NOTE MANAGEMENT
        // ------------------------

        //public async Task<List<string>> GetNotes(string campaignId)
        //{
        //    List<string> notes = new List<string>();
        //    var campaign = await _repository.GetByIdAsync(campaignId);

        //    if (campaign == null || campaign.NoteIds.IsNullOrEmpty()) return notes;

        //    // TODO: Make notes in db.
        //}

        public async Task<Campaign?> AddNoteAsync(string campaignId, string noteId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null || campaign.NoteIds.Contains(noteId)) return campaign;

            campaign.NoteIds.Add(noteId);
            return await _repository.UpdateAsync(campaign);
        }

        public async Task<Campaign?> RemoveNoteAsync(string campaignId, string noteId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null) return null;

            campaign.NoteIds.Remove(noteId);
            return await _repository.UpdateAsync(campaign);
        }

        // ------------------------
        // SESSION MANAGEMENT
        // ------------------------
        public async Task<Campaign?> AddSessionAsync(string campaignId, string sessionId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null || campaign.SessionIds.Contains(sessionId)) return campaign;

            campaign.SessionIds.Add(sessionId);
            return await _repository.UpdateAsync(campaign);
        }

        public async Task<Campaign?> RemoveSessionAsync(string campaignId, string sessionId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null) return null;

            campaign.SessionIds.Remove(sessionId);
            if (campaign.CurrentSessionId == sessionId)
                campaign.CurrentSessionId = null;

            return await _repository.UpdateAsync(campaign);
        }

        public async Task<Campaign?> SetCurrentSessionAsync(string campaignId, string sessionId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null) return null;

            campaign.CurrentSessionId = sessionId;
            return await _repository.UpdateAsync(campaign);
        }

        public async Task<Campaign?> SetActiveEncounterAsync(string campaignId, string? encounterId)
        {
            Guard.NotNullOrWhiteSpace(campaignId, nameof(campaignId));

            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null)
                return null;

            if (!string.IsNullOrWhiteSpace(encounterId))
            {
                var encounter = await _encounterRepository.GetByIdAsync(encounterId);
                if (encounter == null)
                    throw new InvalidOperationException($"Encounter not found: {encounterId}");

                Guard.That(
                    string.Equals(encounter.CampaignId, campaignId, StringComparison.Ordinal),
                    "Encounter must belong to the same campaign.",
                    nameof(encounterId));
            }

            campaign.ActiveEncounterId = encounterId;

            var updated = await _repository.UpdateAsync(campaign);
            if (updated != null)
            {
                await BroadcastCampaignActiveEncounterChangedAsync(updated);
            }

            return updated;
        }

        public async Task<CampaignOverviewDto?> GetOverviewForCharacterAsync(string characterId)
        {
            Guard.NotNullOrWhiteSpace(characterId, nameof(characterId));

            var character = await _characterRepository.GetByIdAsync(characterId);
            if (character == null)
                return null;

            if (character is IOwnedResource owned)
                await EnsureOwnershipAccess(owned);

            if (string.IsNullOrWhiteSpace(character.CampaignId))
                return null;

            var campaign = await _repository.GetByIdAsync(character.CampaignId);
            if (campaign == null)
                return null;

            var campaignCharacters = campaign.CharacterIds.IsNullOrEmpty()
                ? new List<Character>()
                : await _characterRepository.GetByIdsAsync(campaign.CharacterIds);

            return MapToOverviewDto(campaign, campaignCharacters);
        }

        private static CampaignOverviewDto MapToOverviewDto(Campaign campaign, List<Character> characters)
        {
            return new CampaignOverviewDto
            {
                Id = campaign.Id,
                Name = campaign.Name,
                Description = campaign.Description,
                OwnerIds = campaign.OwnerIds ?? new List<string>(),
                CurrentSessionId = campaign.CurrentSessionId,
                QuestIds = campaign.QuestIds ?? new List<string>(),
                Characters = characters.Select(character => new CampaignCharacterDto
                {
                    Id = character.Id,
                    Name = character.Name,
                    IsDead = character.IsDead,
                    IsNPC = character.IsNPC
                }).ToList()
            };
        }

        private async Task BroadcastCampaignActiveEncounterChangedAsync(Campaign campaign)
        {
            var recipients = (campaign.OwnerIds ?? new List<string>()).Distinct().ToList();
            if (recipients.Count == 0)
                return;

            var user = await _authService.GetUserFromTokenAsync();

            await _entitySyncService.BroadcastToUsers(
                "EntityChanged",
                new
                {
                    entityType = "Campaign",
                    entityId = campaign.Id,
                    action = "activeEncounterChanged",
                    data = new
                    {
                        campaignId = campaign.Id,
                        activeEncounterId = campaign.ActiveEncounterId
                    },
                    changedBy = user.Username,
                    timestamp = DateTime.UtcNow
                },
                recipients,
                excludeUserId: user.Id);
        }
    }
}
