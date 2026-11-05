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
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class SessionService : BaseService<Session, ISessionRepository>, ISessionService
    {
        private readonly IEntitySyncService _entitySyncService;
        private readonly ICampaignService _campaignService;

        public SessionService(
            ISessionRepository repository,
            ILogger logger,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            IEntitySyncService entitySyncService,
            ICampaignService campaignService)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _entitySyncService = Guard.NotNull(entitySyncService, nameof(entitySyncService));
            _campaignService = Guard.NotNull(campaignService, nameof(campaignService));
        }

        public async Task<List<Session>> GetByCampaignIdAsync(string campaignId)
        {
            Guard.NotNullOrWhiteSpace(campaignId, nameof(campaignId));
            await Access.EnsureMemberAsync(campaignId);

            var isDm = await Access.IsDmAsync(campaignId);
            var sessions = await _repository.GetByCampaignIdAsync(campaignId);
            return sessions.OrderBy(s => s.Number).Select(s => ForViewer(s, isDm)).ToList();
        }

        public async Task<Session> GetVisibleAsync(string id)
        {
            var session = await _repository.GetByIdAsync(id) ?? throw new NotFoundException("Session not found.");
            await Access.EnsureMemberAsync(session.CampaignId);
            return ForViewer(session, await Access.IsDmAsync(session.CampaignId));
        }

        public async Task<Session> CreateAndNotifyAsync(Session input)
        {
            Guard.NotNull(input, nameof(input));
            var campaignId = string.IsNullOrWhiteSpace(input.CampaignId) ? Access.CurrentCampaignId : input.CampaignId;
            await Access.EnsureDmAsync(campaignId);

            var existing = await _repository.GetByCampaignIdAsync(campaignId!);
            var number = existing.Count == 0 ? 1 : existing.Max(s => s.Number) + 1;

            var session = new Session
            {
                CampaignId = campaignId,
                Number = number,
                Name = string.IsNullOrWhiteSpace(input.Name) ? $"Session {number}" : input.Name.Trim(),
                Location = input.Location,
                ScheduledFor = input.ScheduledFor,
                Plan = input.Plan,
                Recap = input.Recap,
                CreatedBy = Access.UserId,
            };

            var created = await CreateAsync(session) ?? throw new InvalidOperationException("Failed to create session.");
            await _campaignService.AddSessionAsync(created.CampaignId!, created.Id!);
            await BroadcastAsync(created, "created");
            return created;
        }

        public async Task<Session> UpdateDetailsAsync(string id, Session input)
        {
            Guard.NotNull(input, nameof(input));
            var session = await LoadForDmAsync(id);

            if (!string.IsNullOrWhiteSpace(input.Name)) session.Name = input.Name.Trim();
            if (input.Number > 0) session.Number = input.Number;
            session.Location = input.Location;
            session.ScheduledFor = input.ScheduledFor;
            session.Plan = input.Plan;
            session.Recap = input.Recap;

            return await SaveAsync(session);
        }

        public async Task<Session> StartAsync(string id)
        {
            var session = await LoadForDmAsync(id);
            var now = DateTime.UtcNow;

            var others = await _repository.GetByCampaignIdAsync(session.CampaignId!);
            foreach (var live in others.Where(s => s.Status == SessionStatus.Live && s.Id != session.Id))
            {
                live.Status = SessionStatus.Completed;
                live.EndedAt = now;
                await _repository.ReplaceAsync(live);
            }

            session.Status = SessionStatus.Live;
            session.StartedAt ??= now;
            session.EndedAt = null;
            await _campaignService.SetCurrentSessionAsync(session.CampaignId!, session.Id!);
            return await SaveAsync(session);
        }

        public async Task<Session> EndAsync(string id)
        {
            var session = await LoadForDmAsync(id);
            if (session.Status != SessionStatus.Live)
                throw new InvalidOperationException("Only a live session can be ended.");

            session.Status = SessionStatus.Completed;
            session.EndedAt = DateTime.UtcNow;
            return await SaveAsync(session);
        }

        public async Task<bool> DeleteAndNotifyAsync(string id)
        {
            var session = await LoadForDmAsync(id);
            if (!await _repository.DeleteAsync(id))
                return false;

            await _campaignService.RemoveSessionAsync(session.CampaignId!, id);
            await BroadcastAsync(session, "deleted");
            return true;
        }

        public async Task<Session> AddNoteAsync(string id, SessionNoteInput input)
        {
            var session = await LoadForDmAsync(id);
            session.Notes.Add(new SessionNote
            {
                Content = RequireContent(input),
                DmOnly = input.DmOnly,
                Author = _user.FindFirstValue(ClaimTypes.Name),
            });
            return await SaveAsync(session);
        }

        public async Task<Session> UpdateNoteAsync(string id, string noteId, SessionNoteInput input)
        {
            var session = await LoadForDmAsync(id);
            var note = session.Notes.FirstOrDefault(n => n.Id == noteId) ?? throw new NotFoundException("Note not found.");
            note.Content = RequireContent(input);
            note.DmOnly = input.DmOnly;
            note.UpdatedAt = DateTime.UtcNow;
            return await SaveAsync(session);
        }

        public async Task<Session> DeleteNoteAsync(string id, string noteId)
        {
            var session = await LoadForDmAsync(id);
            if (session.Notes.RemoveAll(n => n.Id == noteId) == 0)
                throw new NotFoundException("Note not found.");
            return await SaveAsync(session);
        }

        private static Session ForViewer(Session session, bool isDm)
        {
            if (isDm) return session;
            session.Plan = null;
            session.Notes = session.Notes.Where(n => !n.DmOnly).ToList();
            return session;
        }

        private static string RequireContent(SessionNoteInput? input)
        {
            var content = input?.Content?.Trim();
            if (string.IsNullOrEmpty(content))
                throw new ArgumentException("A note needs some text.");
            return content;
        }

        private async Task<Session> LoadForDmAsync(string id)
        {
            Guard.NotNullOrWhiteSpace(id, nameof(id));
            var session = await _repository.GetByIdAsync(id) ?? throw new NotFoundException("Session not found.");
            await Access.EnsureDmAsync(session.CampaignId);
            return session;
        }

        private async Task<Session> SaveAsync(Session session)
        {
            var saved = await _repository.ReplaceAsync(session);
            await BroadcastAsync(saved, "updated");
            return saved;
        }

        // Players get a filtered view, so the event only says "reload"; every client refetches what it may see.
        private async Task BroadcastAsync(Session session, string action)
        {
            var campaign = await Access.GetCampaignAsync(session.CampaignId);
            if (campaign?.Members == null) return;

            await _entitySyncService.BroadcastToUsers(
                "EntityChanged",
                new
                {
                    entityType = "Session",
                    entityId = session.Id,
                    action,
                    data = new { campaignId = session.CampaignId },
                    changedBy = _user.FindFirstValue(ClaimTypes.Name),
                    timestamp = DateTime.UtcNow
                },
                campaign.Members.Select(m => m.UserId).ToList(),
                Access.UserId);
        }
    }
}
