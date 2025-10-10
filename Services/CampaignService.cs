using dndhelper.Models;
using dndhelper.Repositories;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class CampaignService : BaseService<Campaign, ICampaignRepository>, ICampaignService
    {
        private readonly IUserRepository _userRepository;
        public CampaignService(ICampaignRepository repository, ILogger logger, IUserRepository userRepository) : base(repository, logger)
        {
            _userRepository = userRepository;
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
            campaign.DungeonMasterId = userId;

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

            if (campaign.DungeonMasterId != userId)
                throw CustomExceptions.ThrowCustomException(_logger, "User is not authorized to delete this campaign.");

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

        // ------------------------
        // PLAYER MANAGEMENT
        // ------------------------
        public async Task<Campaign?> AddPlayerAsync(string campaignId, string playerId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null || campaign.PlayerIds.Contains(playerId)) return campaign;

            campaign.PlayerIds.Add(playerId);
            return await _repository.UpdateAsync(campaign);
        }

        public async Task<Campaign?> RemovePlayerAsync(string campaignId, string playerId)
        {
            var campaign = await _repository.GetByIdAsync(campaignId);
            if (campaign == null) return null;

            campaign.PlayerIds.Remove(playerId);
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
    }
}
