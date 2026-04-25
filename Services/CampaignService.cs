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
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class CampaignService : BaseService<Campaign, ICampaignRepository>, ICampaignService
    {
        private readonly IUserRepository _userRepository;
        private readonly ICharacterRepository _characterRepository;
        private readonly IEncounterRepository _encounterRepository;
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
            IEntitySyncService entitySyncService,
            IAuthService authService) : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _userRepository = Guard.NotNull(userRepository, nameof(userRepository));
            _characterRepository = Guard.NotNull(characterRepository, nameof(characterRepository));
            _encounterRepository = Guard.NotNull(encounterRepository, nameof(encounterRepository));
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
            if (campaign.OwnerIds.IsNullOrEmpty())
                campaign.OwnerIds = new List<string>();

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

            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null)
                throw CustomExceptions.ThrowCustomException(_logger, $"Campaign not found with ID: {campaignId}");

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
            List<User> users = new List<User>();
            var campaign = await _repository.GetByIdAsync(campaignId);

            if (campaign == null || campaign.OwnerIds.IsNullOrEmpty())
                return new List<string> { };

            users = await _userRepository.GetByIdsAsync(campaign.OwnerIds!);

            return users.Select(x => x.Id).ToList();
        }

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

        public async Task<Campaign?> AddCharacterAsync(string campaignId, string characterId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null || campaign.CharacterIds.Contains(characterId)) return campaign;

            campaign.CharacterIds.Add(characterId);
            return await _repository.UpdateAsync(campaign);
        }

        public async Task<Campaign?> RemoveCharacterAsync(string campaignId, string characterId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null) return null;

            campaign.CharacterIds.Remove(characterId);
            return await _repository.UpdateAsync(campaign);
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
