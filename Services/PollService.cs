using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    /// <summary>
    /// Campaign polls: the DM manages them, members vote. Every write is a read-modify-replace guarded by
    /// Poll.Version, so concurrent votes never overwrite each other.
    /// </summary>
    public class PollService : BaseService<Poll, IPollRepository>, IPollService
    {
        private const int MaxOptions = 20;
        private const int MaxTextLength = 120;
        private const int MaxDescriptionLength = 2000;

        private readonly IEntitySyncService _entitySyncService;

        public PollService(
            IPollRepository repository,
            ILogger logger,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            IEntitySyncService entitySyncService)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _entitySyncService = Guard.NotNull(entitySyncService, nameof(entitySyncService));
        }

        private string Username => _user.Identity?.Name ?? "Someone";

        public async Task<List<PollView>> GetByCampaignAsync(string campaignId)
        {
            var campaign = await Access.EnsureMemberAsync(campaignId);
            var views = new List<PollView>();
            foreach (var poll in await _repository.GetByCampaignIdAsync(campaignId))
                views.Add(ToView(await CloseIfExpiredAsync(poll, campaign), campaign));
            return views;
        }

        public async Task<PollView> GetPollAsync(string id)
        {
            var poll = await LoadAsync(id);
            var campaign = await Access.EnsureMemberAsync(poll.CampaignId);
            return ToView(await CloseIfExpiredAsync(poll, campaign), campaign);
        }

        public async Task<PollView> CreatePollAsync(PollUpsertRequest request)
        {
            Guard.NotNull(request, nameof(request));
            var campaign = await Access.EnsureDmAsync(request.CampaignId ?? Access.CurrentCampaignId);

            var poll = new Poll { CampaignId = campaign.Id, CreatedBy = Username };
            Apply(poll, request);

            var created = await CreateAsync(poll) ?? throw new InvalidOperationException("Failed to create the poll.");
            await BroadcastAsync(created, "created");
            return ToView(created, campaign);
        }

        public async Task<PollView> UpdatePollAsync(string id, PollUpsertRequest request)
        {
            Guard.NotNull(request, nameof(request));
            var campaign = await Access.EnsureDmAsync((await LoadAsync(id)).CampaignId);

            var (updated, _) = await MutateAsync(id, poll => { Apply(poll, request); return true; });
            await BroadcastAsync(updated, "updated");
            return ToView(updated, campaign);
        }

        public async Task<bool> DeletePollAsync(string id)
        {
            var poll = await LoadAsync(id);
            await Access.EnsureDmAsync(poll.CampaignId);

            if (!await _repository.DeleteAsync(id))
                return false;

            await BroadcastAsync(poll, "deleted");
            return true;
        }

        public async Task<PollView> VoteAsync(string id, PollVoteRequest request)
        {
            var campaign = await Access.EnsureMemberAsync((await LoadAsync(id)).CampaignId);
            var userId = GetCurrentUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
            var picked = (request?.OptionIds ?? new()).Distinct().ToList();

            var (updated, _) = await MutateAsync(id, poll =>
            {
                EnsureOpen(poll);
                Require(picked.Count > 0, "Pick at least one option.");
                Require(picked.All(o => poll.Options.Any(x => x.Id == o)), "That option no longer exists.");
                Require(poll.AllowMultiple || picked.Count == 1, "This poll allows only one choice.");
                Require(poll.MaxChoices == null || picked.Count <= poll.MaxChoices, $"Pick at most {poll.MaxChoices} options.");
                Require(poll.AllowVoteChange || poll.Votes.All(v => v.UserId != userId), "Votes on this poll can't be changed.");

                poll.Votes.RemoveAll(v => v.UserId == userId);
                poll.Votes.Add(new PollVote { UserId = userId, Username = Username, OptionIds = picked, VotedAt = DateTime.UtcNow });
                return true;
            });

            await BroadcastAsync(updated, "updated");
            return ToView(updated, campaign);
        }

        public async Task<PollView> RetractVoteAsync(string id)
        {
            var campaign = await Access.EnsureMemberAsync((await LoadAsync(id)).CampaignId);
            var userId = GetCurrentUserId();

            var (updated, changed) = await MutateAsync(id, poll =>
            {
                EnsureOpen(poll);
                Require(poll.AllowVoteChange, "Votes on this poll can't be changed.");
                return poll.Votes.RemoveAll(v => v.UserId == userId) > 0;
            });

            if (changed) await BroadcastAsync(updated, "updated");
            return ToView(updated, campaign);
        }

        public async Task<PollView> SuggestOptionAsync(string id, PollSuggestionRequest request)
        {
            var campaign = await Access.EnsureMemberAsync((await LoadAsync(id)).CampaignId);
            var isDm = await Access.IsDmAsync(campaign.Id);
            var text = request?.Text?.Trim() ?? string.Empty;
            Require(text.Length is > 0 and <= MaxTextLength, $"Options are 1 to {MaxTextLength} characters.");

            var (updated, _) = await MutateAsync(id, poll =>
            {
                EnsureOpen(poll);
                Require(poll.AllowSuggestions || isDm, "This poll doesn't take suggestions.");
                Require(poll.Options.Count < MaxOptions, $"A poll can have at most {MaxOptions} options.");
                Require(poll.Options.All(o => !string.Equals(o.Text, text, StringComparison.OrdinalIgnoreCase)), "That option already exists.");

                poll.Options.Add(new PollOption { Id = NewId(), Text = text, SuggestedBy = isDm ? null : Username });
                return true;
            });

            await BroadcastAsync(updated, "updated");
            return ToView(updated, campaign);
        }

        public async Task<PollView> ClosePollAsync(string id)
        {
            var campaign = await Access.EnsureDmAsync((await LoadAsync(id)).CampaignId);
            return ToView(await CloseAsync(id, campaign), campaign);
        }

        public async Task<PollView> ReopenPollAsync(string id)
        {
            var campaign = await Access.EnsureDmAsync((await LoadAsync(id)).CampaignId);

            var (updated, changed) = await MutateAsync(id, poll =>
            {
                if (!poll.IsClosed) return false;
                poll.IsClosed = false;
                poll.ClosedAt = null;
                // A passed deadline would close it again on the next read.
                if (poll.ClosesAt <= DateTime.UtcNow) poll.ClosesAt = null;
                return true;
            });

            if (changed) await BroadcastAsync(updated, "updated");
            return ToView(updated, campaign);
        }

        public async Task AnnounceAsync(string id)
        {
            var poll = await LoadAsync(id);
            var campaign = await Access.EnsureDmAsync(poll.CampaignId);
            Require(poll.IsClosed, "Close the poll before announcing its result.");
            await AnnounceAsync(poll, campaign);
        }

        #region Helpers

        private async Task<Poll> LoadAsync(string id)
        {
            Guard.NotNullOrWhiteSpace(id, nameof(id));
            return await GetByIdAsync(id) ?? throw new NotFoundException("Poll not found.");
        }

        /// <summary>Reload, apply, replace if unchanged since the reload; retried on conflicts. The mutation returns
        /// false when there is nothing to write.</summary>
        private async Task<(Poll poll, bool changed)> MutateAsync(string id, Func<Poll, bool> mutate)
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var poll = await _repository.GetFreshAsync(id) ?? throw new NotFoundException("Poll not found.");
                var version = poll.Version;
                if (!mutate(poll)) return (poll, false);
                if (await _repository.TryReplaceAsync(poll, version)) return (poll, true);

                // Jittered backoff so a burst of simultaneous votes spreads out instead of colliding again.
                await Task.Delay(Random.Shared.Next(5, 20 + attempt * 15));
            }

            throw new ConcurrencyException("The poll is changing too quickly. Please try again.");
        }

        private async Task<Poll> CloseAsync(string id, Campaign campaign)
        {
            var (poll, changed) = await MutateAsync(id, p =>
            {
                if (p.IsClosed) return false;
                var now = DateTime.UtcNow;
                p.IsClosed = true;
                p.ClosedAt = p.ClosesAt is { } deadline && deadline <= now ? deadline : now;
                return true;
            });

            // Only the request that actually closed it announces, so concurrent closers don't double-notify.
            if (changed)
            {
                await BroadcastAsync(poll, "updated");
                if (poll.AnnounceOnClose) await AnnounceAsync(poll, campaign);
            }

            return poll;
        }

        // ponytail: deadlines close lazily on the next read of the poll; add a hosted timer if an exact close time matters.
        private async Task<Poll> CloseIfExpiredAsync(Poll poll, Campaign campaign) =>
            poll.IsOpenAt(DateTime.UtcNow) || poll.IsClosed ? poll : await CloseAsync(poll.Id!, campaign);

        private void Apply(Poll poll, PollUpsertRequest request)
        {
            var title = request.Title?.Trim() ?? string.Empty;
            Require(title.Length is > 0 and <= MaxTextLength, $"The title is required ({MaxTextLength} characters at most).");

            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            Require(description == null || description.Length <= MaxDescriptionLength, $"The description is at most {MaxDescriptionLength} characters.");

            var options = new List<PollOption>();
            foreach (var option in request.Options ?? new())
            {
                var text = option.Text?.Trim() ?? string.Empty;
                if (text.Length == 0) continue;
                Require(text.Length <= MaxTextLength, $"Options are at most {MaxTextLength} characters.");

                var existing = poll.Options.FirstOrDefault(o => o.Id == option.Id);
                options.Add(new PollOption { Id = existing?.Id ?? NewId(), Text = text, SuggestedBy = existing?.SuggestedBy });
            }

            Require(options.Count is >= 2 and <= MaxOptions, $"A poll needs 2 to {MaxOptions} options.");
            Require(options.Select(o => o.Text.ToLowerInvariant()).Distinct().Count() == options.Count, "Options must be unique.");

            var maxChoices = request.AllowMultiple ? request.MaxChoices : null;
            Require(maxChoices == null || (maxChoices >= 1 && maxChoices <= options.Count), "Max choices must be between 1 and the number of options.");

            if (request.ClosesAt != null && request.ClosesAt != poll.ClosesAt)
                Require(request.ClosesAt > DateTime.UtcNow, "The deadline must be in the future.");

            poll.Title = title;
            poll.Description = description;
            poll.Options = options;
            poll.AllowMultiple = request.AllowMultiple;
            poll.MaxChoices = maxChoices;
            poll.Anonymous = request.Anonymous;
            poll.ResultsVisibility = request.ResultsVisibility;
            poll.AllowVoteChange = request.AllowVoteChange;
            poll.AllowSuggestions = request.AllowSuggestions;
            poll.AnnounceOnClose = request.AnnounceOnClose;
            poll.ClosesAt = request.ClosesAt;

            // Keep votes consistent with the edited options and choice limits.
            var ids = options.Select(o => o.Id).ToHashSet();
            var limit = !poll.AllowMultiple ? 1 : poll.MaxChoices ?? int.MaxValue;
            foreach (var vote in poll.Votes)
                vote.OptionIds = vote.OptionIds.Where(ids.Contains).Take(limit).ToList();
            poll.Votes.RemoveAll(v => v.OptionIds.Count == 0);
        }

        private PollView ToView(Poll poll, Campaign campaign)
        {
            var userId = GetCurrentUserId();
            var isDm = Access.IsSuperAdmin || campaign.IsDm(userId);
            var mine = poll.Votes.FirstOrDefault(v => v.UserId == userId);
            var hidden = !isDm && !poll.IsClosed && (poll.ResultsVisibility == PollResultsVisibility.AfterClose
                || (poll.ResultsVisibility == PollResultsVisibility.AfterVote && mine == null));

            return new PollView
            {
                Id = poll.Id!,
                CampaignId = poll.CampaignId!,
                Title = poll.Title,
                Description = poll.Description,
                Options = poll.Options,
                AllowMultiple = poll.AllowMultiple,
                MaxChoices = poll.MaxChoices,
                Anonymous = poll.Anonymous,
                ResultsVisibility = poll.ResultsVisibility,
                AllowVoteChange = poll.AllowVoteChange,
                AllowSuggestions = poll.AllowSuggestions,
                AnnounceOnClose = poll.AnnounceOnClose,
                ClosesAt = poll.ClosesAt,
                IsClosed = poll.IsClosed,
                ClosedAt = poll.ClosedAt,
                CreatedBy = poll.CreatedBy,
                CreatedAt = poll.CreatedAt,
                UpdatedAt = poll.UpdatedAt,
                MyOptionIds = mine?.OptionIds ?? new(),
                VoterCount = poll.Votes.Count,
                MemberCount = campaign.Members?.Count ?? 0,
                ResultsHidden = hidden,
                Tally = hidden ? null : Tally(poll),
                Voters = hidden || poll.Anonymous
                    ? null
                    : poll.Votes.OrderBy(v => v.VotedAt)
                        .Select(v => new PollVoterView { Username = v.Username, OptionIds = v.OptionIds, VotedAt = v.VotedAt })
                        .ToList(),
            };
        }

        private static Dictionary<string, int> Tally(Poll poll) =>
            poll.Options.ToDictionary(o => o.Id, o => poll.Votes.Count(v => v.OptionIds.Contains(o.Id)));

        /// <summary>Clients refetch the poll on these events: each member's view differs, so no poll data is pushed.</summary>
        private async Task BroadcastAsync(Poll poll, string action, object? data = null)
        {
            var campaign = await Access.GetCampaignAsync(poll.CampaignId);
            if (campaign?.Members == null || campaign.Members.Count == 0) return;

            await _entitySyncService.BroadcastToUsers(
                "EntityChanged",
                new
                {
                    entityType = "Poll",
                    entityId = poll.Id,
                    action,
                    data = data ?? new { id = poll.Id, campaignId = poll.CampaignId },
                    changedBy = Username,
                    timestamp = DateTime.UtcNow
                },
                campaign.Members.Select(m => m.UserId).ToList());
        }

        private async Task AnnounceAsync(Poll poll, Campaign campaign)
        {
            var tally = Tally(poll);
            var top = tally.Values.DefaultIfEmpty(0).Max();
            var winners = poll.Options.Where(o => top > 0 && tally[o.Id] == top).Select(o => o.Text).ToList();
            var voters = poll.Votes.Count;

            var summary = winners.Count switch
            {
                0 => "No votes were cast.",
                1 => $"{winners[0]} won with {top} of {voters} vote{(voters == 1 ? "" : "s")}.",
                _ => $"Tie between {string.Join(", ", winners)} ({top} vote{(top == 1 ? "" : "s")} each)."
            };

            await BroadcastAsync(poll, "announced", new { id = poll.Id, campaignId = poll.CampaignId, title = poll.Title, summary, winners });
        }

        private static void EnsureOpen(Poll poll) => Require(poll.IsOpenAt(DateTime.UtcNow), "This poll is closed.");

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static string NewId() => ObjectId.GenerateNewId().ToString();

        #endregion
    }
}
