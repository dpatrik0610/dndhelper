using dndhelper.Authentication.Interfaces;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class SessionService : BaseService<Session, ISessionRepository>, ISessionService
    {
        private readonly IEntitySyncService _entitySyncService;
        private readonly ICampaignService _campaignService;
        private readonly IAuthService _authService;

        public SessionService(
            ISessionRepository repository,
            ILogger logger,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            IEntitySyncService entitySyncService,
            ICampaignService campaignService,
            IAuthService authService)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _entitySyncService = entitySyncService ?? throw new ArgumentNullException(nameof(entitySyncService));
            _campaignService = campaignService ?? throw new ArgumentNullException(nameof(campaignService));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        }

        public async Task<IEnumerable<Session>> GetByCampaignIdAsync(string campaignId)
        {
            if (string.IsNullOrWhiteSpace(campaignId))
                throw new ArgumentNullException(nameof(campaignId));

            var sessions = await _repository.GetByCampaignIdAsync(campaignId);
            return await FilterOwnedResourcesAsync(sessions);
        }

        public async Task<Session?> CreateAndNotifyAsync(Session session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (string.IsNullOrWhiteSpace(session.CampaignId))
                throw new ArgumentException("CampaignId is required to create a session.", nameof(session.CampaignId));

            try
            {
                var created = await CreateAsync(session);
                if (created != null)
                {
                    // Attach to campaign session list
                    await _campaignService.AddSessionAsync(created.CampaignId, created.Id);

                    if (created.IsLive)
                    {
                        await _campaignService.SetCurrentSessionAsync(created.CampaignId, created.Id);
                    }

                    await BroadcastSessionChangeAsync(created, "created", created);
                }

                return created;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating session for campaign {CampaignId}", session.CampaignId);
                throw;
            }
        }

        public async Task<Session?> UpdateAndNotifyAsync(string id, Session session)
        {
            try
            {
                session.Id = id;
                var updated = await UpdateAsync(session);
                if (updated != null)
                {
                    if (updated.IsLive)
                    {
                        await _campaignService.SetCurrentSessionAsync(updated.CampaignId, updated.Id);
                    }

                    await BroadcastSessionChangeAsync(updated, "updated", updated);
                }

                return updated;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating session {SessionId}", id);
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
                    // Detach from campaign
                    if (!string.IsNullOrWhiteSpace(existing.CampaignId))
                    {
                        await _campaignService.RemoveSessionAsync(existing.CampaignId, id);
                    }

                    await BroadcastSessionChangeAsync(existing, "deleted", new { id });
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting session {SessionId}", id);
                throw;
            }
        }

        private async Task BroadcastSessionChangeAsync(Session session, string action, object data)
        {
            var recipients = new HashSet<string>(session.OwnerIds ?? Enumerable.Empty<string>());

            if (!string.IsNullOrEmpty(session.CampaignId))
            {
                var dmIds = await _campaignService.GetCampaignDMIdsAsync(session.CampaignId);
                if (dmIds != null)
                {
                    foreach (var dm in dmIds)
                        recipients.Add(dm);
                }
            }

            if (!recipients.Any())
                return;

            var user = await _authService.GetUserFromTokenAsync();

            await _entitySyncService.BroadcastToUsers(
                "EntityChanged",
                new
                {
                    entityType = "Session",
                    entityId = session.Id,
                    action,
                    data,
                    changedBy = user.Username,
                    timestamp = DateTime.UtcNow
                },
                recipients.ToList(),
                excludeUserId: user.Id
            );
        }
    }
}
