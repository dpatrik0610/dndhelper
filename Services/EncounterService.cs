using dndhelper.Authentication.Interfaces;
using dndhelper.Models;
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
    public class EncounterService : BaseService<Encounter, IEncounterRepository>, IEncounterService
    {
        private readonly IEntitySyncService _entitySyncService;
        private readonly ICampaignService _campaignService;
        private readonly ISessionService _sessionService;
        private readonly IAuthService _authService;

        public EncounterService(
            IEncounterRepository repository,
            ILogger logger,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            IEntitySyncService entitySyncService,
            ICampaignService campaignService,
            ISessionService sessionService,
            IAuthService authService)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _entitySyncService = Guard.NotNull(entitySyncService, nameof(entitySyncService));
            _campaignService = Guard.NotNull(campaignService, nameof(campaignService));
            _sessionService = Guard.NotNull(sessionService, nameof(sessionService));
            _authService = Guard.NotNull(authService, nameof(authService));
        }

        public async Task<IEnumerable<Encounter>> GetByCampaignIdAsync(string campaignId)
        {
            Guard.NotNullOrWhiteSpace(campaignId, nameof(campaignId));

            var encounters = await _repository.GetByCampaignIdAsync(campaignId);
            return await FilterOwnedResourcesAsync(encounters);
        }

        public async Task<IEnumerable<Encounter>> GetBySessionIdAsync(string sessionId)
        {
            Guard.NotNullOrWhiteSpace(sessionId, nameof(sessionId));

            var encounters = await _repository.GetBySessionIdAsync(sessionId);
            return await FilterOwnedResourcesAsync(encounters);
        }

        public async Task<Encounter?> CreateAndNotifyAsync(Encounter encounter)
        {
            Guard.NotNull(encounter, nameof(encounter));
            Guard.NotNullOrWhiteSpace(encounter.CampaignId, nameof(encounter.CampaignId));

            await EnsureCampaignExistsAsync(encounter.CampaignId);
            await EnsureSessionConsistencyAsync(encounter.CampaignId, encounter.SessionId);

            try
            {
                var created = await CreateAsync(encounter);
                if (created != null)
                {
                    await AddEncounterToSessionAsync(created.SessionId, created.Id);
                    await BroadcastEncounterChangeAsync(created, "created", created);
                }

                return created;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating encounter for campaign {CampaignId}", encounter.CampaignId);
                throw;
            }
        }

        public async Task<Encounter?> UpdateAndNotifyAsync(string id, Encounter encounter)
        {
            Guard.NotNullOrWhiteSpace(id, nameof(id));
            Guard.NotNull(encounter, nameof(encounter));
            Guard.NotNullOrWhiteSpace(encounter.CampaignId, nameof(encounter.CampaignId));

            var existing = await GetByIdAsync(id);
            if (existing == null)
                return null;

            try
            {
                encounter.Id = id;

                await EnsureCampaignExistsAsync(encounter.CampaignId);
                await EnsureSessionConsistencyAsync(encounter.CampaignId, encounter.SessionId);

                var updated = await UpdateAsync(encounter);
                if (updated != null)
                {
                    await SyncSessionEncounterReferenceAsync(existing, updated);
                    await BroadcastEncounterChangeAsync(updated, "updated", updated);
                }

                return updated;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating encounter {EncounterId}", id);
                throw;
            }
        }

        public async Task<bool> DeleteAndNotifyAsync(string id)
        {
            var existing = await GetByIdAsync(id);
            if (existing == null)
                return false;

            try
            {
                var success = await DeleteAsync(id);
                if (success)
                {
                    await RemoveEncounterFromSessionAsync(existing.SessionId, id);
                    await BroadcastEncounterChangeAsync(existing, "deleted", new { id });
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting encounter {EncounterId}", id);
                throw;
            }
        }

        private async Task EnsureCampaignExistsAsync(string campaignId)
        {
            Guard.NotNullOrWhiteSpace(campaignId, nameof(campaignId));

            var campaign = await _campaignService.GetByIdInternalAsync(campaignId);
            if (campaign == null)
                throw new InvalidOperationException($"Campaign not found: {campaignId}");
        }

        private async Task EnsureSessionConsistencyAsync(string campaignId, string? sessionId)
        {
            Guard.NotNullOrWhiteSpace(campaignId, nameof(campaignId));

            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            var session = await _sessionService.GetByIdInternalAsync(sessionId);
            if (session == null)
                throw new InvalidOperationException($"Session not found: {sessionId}");

            if (!string.Equals(session.CampaignId, campaignId, StringComparison.Ordinal))
                throw new InvalidOperationException("Encounter session must belong to the same campaign.");
        }

        private async Task AddEncounterToSessionAsync(string? sessionId, string? encounterId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(encounterId))
                return;

            var session = await _sessionService.GetByIdInternalAsync(sessionId);
            if (session == null)
                return;

            session.EncounterIds ??= new List<string>();
            if (session.EncounterIds.Contains(encounterId))
                return;

            session.EncounterIds.Add(encounterId);
            await _sessionService.UpdateInternalAsync(session);
        }

        private async Task RemoveEncounterFromSessionAsync(string? sessionId, string encounterId)
        {
            Guard.NotNullOrWhiteSpace(encounterId, nameof(encounterId));

            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            var session = await _sessionService.GetByIdInternalAsync(sessionId);
            if (session == null || session.EncounterIds == null || !session.EncounterIds.Contains(encounterId))
                return;

            session.EncounterIds.Remove(encounterId);
            await _sessionService.UpdateInternalAsync(session);
        }

        private async Task SyncSessionEncounterReferenceAsync(Encounter existing, Encounter updated)
        {
            if (string.Equals(existing.SessionId, updated.SessionId, StringComparison.Ordinal))
                return;

            await RemoveEncounterFromSessionAsync(existing.SessionId, existing.Id!);
            await AddEncounterToSessionAsync(updated.SessionId, updated.Id);
        }

        private async Task BroadcastEncounterChangeAsync(Encounter encounter, string action, object data)
        {
            var recipients = new HashSet<string>(encounter.OwnerIds ?? Enumerable.Empty<string>());

            if (!string.IsNullOrWhiteSpace(encounter.CampaignId))
            {
                var dmIds = await _campaignService.GetCampaignDMIdsAsync(encounter.CampaignId);
                foreach (var dmId in dmIds)
                    recipients.Add(dmId);
            }

            if (!recipients.Any())
                return;

            var user = await _authService.GetUserFromTokenAsync();

            await _entitySyncService.BroadcastToUsers(
                "EntityChanged",
                new
                {
                    entityType = "Encounter",
                    entityId = encounter.Id,
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
