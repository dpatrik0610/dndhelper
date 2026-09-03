using dndhelper.Authentication.Interfaces;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.CharacterServices.Interfaces;
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
    public class QuestService : BaseService<Quest, IQuestRepository>, IQuestService
    {
        private readonly ICampaignService _campaignService;
        private readonly IEntitySyncService _entitySyncService;
        private readonly IAuthService _authService;
        private readonly ICharacterService _characterService;

        public QuestService(
            IQuestRepository repository,
            ILogger logger,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            ICampaignService campaignService,
            IEntitySyncService entitySyncService,
            IAuthService authService,
            ICharacterService characterService)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _campaignService = Guard.NotNull(campaignService, nameof(campaignService));
            _entitySyncService = Guard.NotNull(entitySyncService, nameof(entitySyncService));
            _authService = Guard.NotNull(authService, nameof(authService));
            _characterService = Guard.NotNull(characterService, nameof(characterService));
        }

        public async Task<IEnumerable<Quest>> GetByCampaignIdAsync(string campaignId)
        {
            Guard.NotNullOrWhiteSpace(campaignId, nameof(campaignId));

            var quests = await _repository.GetByCampaignIdAsync(campaignId);
            return quests;
        }

        public async Task<Quest?> CreateAndNotifyAsync(Quest quest)
        {
            Guard.NotNull(quest, nameof(quest));
            Guard.NotNullOrWhiteSpace(quest.CampaignId, nameof(quest.CampaignId));

            await EnsureCampaignExistsAsync(quest.CampaignId);
            await EnsureUserCanManageQuestAsync(quest, "create");

            try
            {
                var created = await CreateAsync(quest);
                if (created != null)
                {
                    await _campaignService.AddQuestAsync(created.CampaignId, created.Id!);
                    await BroadcastQuestChangeAsync(created, "created", created);
                }

                return created;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating quest for campaign {CampaignId}", quest.CampaignId);
                throw;
            }
        }

        public async Task<Quest?> UpdateAndNotifyAsync(string id, Quest quest)
        {
            Guard.NotNullOrWhiteSpace(id, nameof(id));
            Guard.NotNull(quest, nameof(quest));
            Guard.NotNullOrWhiteSpace(quest.CampaignId, nameof(quest.CampaignId));

            var existing = await GetByIdAsync(id);
            if (existing == null)
                return null;

            await EnsureUserCanManageQuestAsync(existing, "update");

            // Prevent non-DMs from changing QuestType of a personal quest to a DM-only type
            var userId = GetCurrentUserId();
            var dmIds = await _campaignService.GetCampaignDMIdsAsync(existing.CampaignId);
            bool isDm = (dmIds != null && dmIds.Contains(userId)) || _user.IsInRole("Admin");

            if (!isDm && quest.Type != QuestType.Personal)
            {
                throw new UnauthorizedAccessException("Only campaign DMs can change quest type to non-personal.");
            }

            try
            {
                quest.Id = id;

                await EnsureCampaignExistsAsync(quest.CampaignId);

                var updated = await UpdateAsync(quest);
                if (updated != null)
                {
                    if (!string.Equals(existing.CampaignId, updated.CampaignId, StringComparison.Ordinal))
                    {
                        await _campaignService.RemoveQuestAsync(existing.CampaignId, existing.Id!);
                        await _campaignService.AddQuestAsync(updated.CampaignId, updated.Id!);
                    }

                    await BroadcastQuestChangeAsync(updated, "updated", updated);
                }

                return updated;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating quest {QuestId}", id);
                throw;
            }
        }

        public async Task<bool> DeleteAndNotifyAsync(string id)
        {
            var existing = await GetByIdAsync(id);
            if (existing == null)
                return false;

            await EnsureUserCanManageQuestAsync(existing, "delete");

            try
            {
                var success = await DeleteAsync(id);
                if (success)
                {
                    await _campaignService.RemoveQuestAsync(existing.CampaignId, id);
                    await BroadcastQuestChangeAsync(existing, "deleted", new { id });
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting quest {QuestId}", id);
                throw;
            }
        }

        // Atomic Quest Objectives Management
        public async Task<Quest?> AddObjectiveAsync(string questId, QuestObjective objective)
        {
            Guard.NotNullOrWhiteSpace(questId, nameof(questId));
            Guard.NotNull(objective, nameof(objective));

            var quest = await GetByIdAsync(questId);
            if (quest == null)
                return null;

            if (string.IsNullOrWhiteSpace(objective.Id))
            {
                objective.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
            }

            // Automate completion check
            if (objective.CurrentProgress >= objective.CompletionThreshold)
            {
                objective.IsCompleted = true;
            }

            quest.Objectives ??= new List<QuestObjective>();
            quest.Objectives.Add(objective);

            return await UpdateAndNotifyAsync(questId, quest);
        }

        public async Task<Quest?> UpdateObjectiveAsync(string questId, QuestObjective objective)
        {
            Guard.NotNullOrWhiteSpace(questId, nameof(questId));
            Guard.NotNull(objective, nameof(objective));
            if (string.IsNullOrWhiteSpace(objective.Id))
                throw new ArgumentException("Objective ID cannot be null or empty during update.", nameof(objective));

            var quest = await GetByIdAsync(questId);
            if (quest == null || quest.Objectives == null)
                return null;

            var existingObj = quest.Objectives.FirstOrDefault(o => string.Equals(o.Id, objective.Id, StringComparison.Ordinal));
            if (existingObj == null)
                return null;

            existingObj.Description = objective.Description;
            existingObj.CurrentProgress = objective.CurrentProgress;
            existingObj.CompletionThreshold = objective.CompletionThreshold;

            // Automate completion check
            if (existingObj.CurrentProgress >= existingObj.CompletionThreshold)
            {
                existingObj.IsCompleted = true;
            }
            else
            {
                existingObj.IsCompleted = objective.IsCompleted;
            }

            return await UpdateAndNotifyAsync(questId, quest);
        }

        public async Task<Quest?> DeleteObjectiveAsync(string questId, string objectiveId)
        {
            Guard.NotNullOrWhiteSpace(questId, nameof(questId));
            Guard.NotNullOrWhiteSpace(objectiveId, nameof(objectiveId));

            var quest = await GetByIdAsync(questId);
            if (quest == null || quest.Objectives == null)
                return null;

            var existingObj = quest.Objectives.FirstOrDefault(o => string.Equals(o.Id, objectiveId, StringComparison.Ordinal));
            if (existingObj == null)
                return quest; // Return unmodified quest if objective not found

            quest.Objectives.Remove(existingObj);

            return await UpdateAndNotifyAsync(questId, quest);
        }

        private async Task EnsureCampaignExistsAsync(string campaignId)
        {
            Guard.NotNullOrWhiteSpace(campaignId, nameof(campaignId));

            var campaign = await _campaignService.GetByIdInternalAsync(campaignId);
            if (campaign == null)
                throw new InvalidOperationException($"Campaign not found: {campaignId}");
        }

        private async Task EnsureUserCanManageQuestAsync(Quest quest, string operation)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
                throw new UnauthorizedAccessException("User is not authenticated.");

            // System Admins can manage everything
            if (_user.IsInRole("Admin"))
                return;

            // Get the campaign's DM IDs
            var dmIds = await _campaignService.GetCampaignDMIdsAsync(quest.CampaignId);
            bool isDm = dmIds != null && dmIds.Contains(userId);

            // If the user is a DM of the campaign, they can manage all quests
            if (isDm)
                return;

            // Otherwise, if the quest is NOT personal, non-DMs cannot manage it
            if (quest.Type != QuestType.Personal)
                throw new UnauthorizedAccessException($"Only campaign DMs can {operation} non-personal campaign quests.");

            // For personal quests, let's verify if the user owns at least one of the involved characters
            if (quest.InvolvedCharacterIds == null || !quest.InvolvedCharacterIds.Any())
                throw new UnauthorizedAccessException($"Personal quests must be linked to at least one involved character.");

            // Check character ownership and verify campaign membership
            bool ownsCharacter = false;
            foreach (var charId in quest.InvolvedCharacterIds)
            {
                var character = await _characterService.GetByIdAsync(charId);
                if (character == null)
                    throw new InvalidOperationException($"Involved character not found with ID: {charId}");

                if (!string.Equals(character.CampaignId, quest.CampaignId, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Character '{character.Name}' does not belong to this campaign.");

                if (character.OwnerIds != null && character.OwnerIds.Contains(userId))
                {
                    ownsCharacter = true;
                }
            }

            if (!ownsCharacter)
                throw new UnauthorizedAccessException($"You do not own any of the characters linked to this personal quest.");
        }

        private async Task BroadcastQuestChangeAsync(Quest quest, string action, object data)
        {
            var recipients = new HashSet<string>();

            if (!string.IsNullOrWhiteSpace(quest.CampaignId))
            {
                var dmIds = await _campaignService.GetCampaignDMIdsAsync(quest.CampaignId);
                foreach (var dmId in dmIds)
                    recipients.Add(dmId);

                var characters = await _campaignService.GetCharactersAsync(quest.CampaignId);
                if (characters != null)
                {
                    foreach (var character in characters)
                    {
                        if (character.OwnerIds != null)
                        {
                            foreach (var ownerId in character.OwnerIds)
                                recipients.Add(ownerId);
                        }
                    }
                }
            }

            if (!recipients.Any())
                return;

            var user = await _authService.GetUserFromTokenAsync();

            await _entitySyncService.BroadcastToUsers(
                "EntityChanged",
                new
                {
                    entityType = "Quest",
                    entityId = quest.Id,
                    action,
                    data,
                    changedBy = user.Username,
                    timestamp = DateTime.UtcNow
                },
                recipients.ToList(),
                excludeUserId: user.Id);
        }
    }
}
