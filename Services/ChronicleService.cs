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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    /// <summary>
    /// The DM's campaign notebook. DMs read and write everything; a player only reads the entries shared with them,
    /// directly or through a shared folder above them, stripped of %%secret%% blocks and DM marks, and only the
    /// folders that lead to what they can read. Events carry ids only: every client refetches with its own permissions.
    /// </summary>
    public partial class ChronicleService : BaseService<ChronicleEntry, IChronicleEntryRepository>, IChronicleService
    {
        private const int MaxTitleLength = 200;
        private const int MaxContentLength = 100_000;
        private const int MaxTags = 30;
        private const int MaxTagLength = 40;
        private const int MaxFolderNameLength = 80;
        private const int MaxCommentLength = 4000;
        private const int MaxImagesPerUser = 500;

        private readonly IChronicleFolderRepository _folders;
        private readonly IChronicleCommentRepository _comments;
        private readonly IEntitySyncService _entitySyncService;
        private readonly IFileStorageService _storage;

        public ChronicleService(
            IChronicleEntryRepository repository,
            IChronicleFolderRepository folders,
            IChronicleCommentRepository comments,
            ILogger logger,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            IEntitySyncService entitySyncService,
            IFileStorageService storage)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _folders = Guard.NotNull(folders, nameof(folders));
            _comments = Guard.NotNull(comments, nameof(comments));
            _entitySyncService = Guard.NotNull(entitySyncService, nameof(entitySyncService));
            _storage = Guard.NotNull(storage, nameof(storage));
        }

        private string Username => _user.Identity?.Name ?? "Someone";
        private string UserId => GetCurrentUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");

        [GeneratedRegex(@"%%[\s\S]*?(?:%%|\z)")]
        private static partial Regex SecretBlock();

        public static string StripSecrets(string content) => SecretBlock().Replace(content, string.Empty);

        #region Entries

        public async Task<List<ChronicleEntry>> GetEntriesAsync(string campaignId)
        {
            await Access.EnsureMemberAsync(campaignId);
            var entries = await _repository.GetByCampaignIdAsync(campaignId);
            if (await Access.IsDmAsync(campaignId)) return entries;

            var userId = GetCurrentUserId();
            var shares = await SharesAsync(campaignId);
            return entries.Where(e => shares.CanRead(e, userId)).Select(ToPlayerView).ToList();
        }

        public async Task<ChronicleEntry> GetEntryAsync(string id)
        {
            var entry = await LoadEntryAsync(id);
            return await EnsureCanReadAsync(entry) ? entry : ToPlayerView(entry);
        }

        public async Task<ChronicleEntry> CreateEntryAsync(ChronicleEntryRequest request)
        {
            Guard.NotNull(request, nameof(request));
            var campaign = await Access.EnsureDmAsync(request.CampaignId ?? Access.CurrentCampaignId);

            var entry = new ChronicleEntry { CampaignId = campaign.Id, CreatedBy = Username, CreatedById = GetCurrentUserId() };
            await ApplyAsync(entry, request, campaign);

            var created = await CreateAsync(entry) ?? throw new InvalidOperationException("Failed to create the entry.");
            await BroadcastEntryAsync(campaign, created, null, "created");
            return created;
        }

        public async Task<ChronicleEntry> UpdateEntryAsync(string id, ChronicleEntryRequest request)
        {
            Guard.NotNull(request, nameof(request));
            var entry = await LoadEntryAsync(id);
            var campaign = await Access.EnsureDmAsync(entry.CampaignId);
            var before = new ChronicleEntry { Visibility = entry.Visibility, SharedWith = entry.SharedWith.ToList(), FolderId = entry.FolderId };

            await ApplyAsync(entry, request, campaign);
            entry.UpdatedAt = DateTime.UtcNow;

            var saved = await _repository.UpdateAsync(entry) ?? throw new InvalidOperationException("Failed to save the entry.");
            await BroadcastEntryAsync(campaign, saved, before, "updated");
            return saved;
        }

        public async Task<bool> DeleteEntryAsync(string id)
        {
            var entry = await LoadEntryAsync(id);
            var campaign = await Access.EnsureDmAsync(entry.CampaignId);

            if (!await _repository.DeleteAsync(id)) return false;
            await _comments.DeleteByEntryIdAsync(id);
            await BroadcastEntryAsync(campaign, entry, null, "deleted");
            return true;
        }

        public async Task<string> UploadImageAsync(string campaignId, Stream content, string? fileName)
        {
            await Access.EnsureDmAsync(campaignId);
            var userId = UserId;
            Require(await _storage.CountAsync(userId, StorageFolder.Chronicle) < MaxImagesPerUser,
                $"You've reached the limit of {MaxImagesPerUser} Chronicle images.");

            var file = await _storage.UploadAsync(content, StorageFolder.Chronicle, userId, fileName);
            return file.Url ?? throw new InvalidOperationException("Chronicle images need a public R2 domain (R2__PublicBaseUrl).");
        }

        private async Task ApplyAsync(ChronicleEntry entry, ChronicleEntryRequest request, Campaign campaign)
        {
            var title = request.Title?.Trim() ?? string.Empty;
            Require(title.Length is > 0 and <= MaxTitleLength, $"The title is required ({MaxTitleLength} characters at most).");

            var content = request.Content ?? string.Empty;
            Require(content.Length <= MaxContentLength, $"An entry is at most {MaxContentLength:N0} characters.");

            Require(Enum.IsDefined(request.Category), "Unknown category.");
            Require(Enum.IsDefined(request.Flag), "Unknown flag.");
            Require(Enum.IsDefined(request.Visibility), "Unknown visibility.");

            var tags = (request.Tags ?? new())
                .Select(t => t?.Trim().TrimStart('#').Trim() ?? string.Empty)
                .Where(t => t.Length > 0)
                .DistinctBy(t => t.ToLowerInvariant())
                .ToList();
            Require(tags.Count <= MaxTags, $"An entry has at most {MaxTags} tags.");
            Require(tags.All(t => t.Length <= MaxTagLength), $"Tags are at most {MaxTagLength} characters.");

            var folderId = string.IsNullOrWhiteSpace(request.FolderId) ? null : request.FolderId;
            if (folderId != null) await LoadFolderInAsync(folderId, campaign.Id!);

            entry.Title = title;
            entry.Content = content;
            entry.FolderId = folderId;
            entry.Category = request.Category;
            entry.Tags = tags;
            entry.Pinned = request.Pinned;
            entry.Flag = request.Flag;
            entry.Visibility = request.Visibility;
            entry.SharedWith = SharedWithFor(request.Visibility, request.SharedWith, campaign);
        }

        private static List<string> SharedWithFor(ChronicleVisibility visibility, List<string>? requested, Campaign campaign)
        {
            if (visibility != ChronicleVisibility.Selected) return new List<string>();
            var memberIds = campaign.Members.Select(m => m.UserId).ToHashSet();
            return (requested ?? new()).Where(memberIds.Contains).Distinct().ToList();
        }

        private async Task<ChronicleEntry> LoadEntryAsync(string id)
        {
            Guard.NotNullOrWhiteSpace(id, nameof(id));
            Require(ObjectId.TryParse(id, out _), "Invalid entry id.");
            var entry = await _repository.GetByIdAsync(id);
            return entry == null || entry.IsDeleted ? throw new NotFoundException("Entry not found.") : entry;
        }

        /// <summary>True for a DM. A player who may not see it gets "not found", so hidden entries don't reveal they exist.</summary>
        private async Task<bool> EnsureCanReadAsync(ChronicleEntry entry)
        {
            await Access.EnsureMemberAsync(entry.CampaignId);
            if (await Access.IsDmAsync(entry.CampaignId)) return true;
            if (!(await SharesAsync(entry.CampaignId!)).CanRead(entry, GetCurrentUserId())) throw new NotFoundException("Entry not found.");
            return false;
        }

        private static ChronicleEntry ToPlayerView(ChronicleEntry e) => new()
        {
            Id = e.Id,
            CampaignId = e.CampaignId,
            Title = e.Title,
            Content = StripSecrets(e.Content),
            FolderId = e.FolderId,
            Category = e.Category,
            Tags = e.Tags.ToList(),
            Visibility = ChronicleVisibility.All,
            CreatedBy = e.CreatedBy,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
        };

        #endregion

        #region Folders

        public async Task<List<ChronicleFolder>> GetFoldersAsync(string campaignId)
        {
            await Access.EnsureMemberAsync(campaignId);
            var folders = await _folders.GetByCampaignIdAsync(campaignId);
            if (await Access.IsDmAsync(campaignId)) return folders;

            var userId = GetCurrentUserId();
            var shares = new ShareMap(folders);
            var keep = new HashSet<string>();
            foreach (var entry in await _repository.GetByCampaignIdAsync(campaignId))
                if (shares.CanRead(entry, userId))
                    keep.UnionWith(shares.Chain(entry.FolderId).Select(f => f.Id!));
            foreach (var folder in folders)
                if (shares.CanReadFolder(folder, userId))
                    keep.UnionWith(shares.Chain(folder.Id).Select(f => f.Id!));

            return folders.Where(f => keep.Contains(f.Id!)).Select(f => new ChronicleFolder
            {
                Id = f.Id,
                CampaignId = f.CampaignId,
                Name = f.Name,
                ParentId = f.ParentId,
                Visibility = shares.CanReadFolder(f, userId) ? ChronicleVisibility.All : ChronicleVisibility.Private,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
            }).ToList();
        }

        public async Task<ChronicleFolder> CreateFolderAsync(ChronicleFolderRequest request)
        {
            Guard.NotNull(request, nameof(request));
            var campaign = await Access.EnsureDmAsync(request.CampaignId ?? Access.CurrentCampaignId);

            var folder = new ChronicleFolder { CampaignId = campaign.Id, CreatedAt = DateTime.UtcNow };
            await ApplyFolderAsync(folder, request, campaign);

            var created = await _folders.CreateAsync(folder) ?? throw new InvalidOperationException("Failed to create the folder.");
            await BroadcastFolderAsync(campaign, created, new HashSet<string>(), "created");
            return created;
        }

        public async Task<ChronicleFolder> UpdateFolderAsync(string id, ChronicleFolderRequest request)
        {
            Guard.NotNull(request, nameof(request));
            var folder = await LoadFolderAsync(id);
            var campaign = await Access.EnsureDmAsync(folder.CampaignId);
            var readersBefore = await ReadersAsync(campaign, folder);

            await ApplyFolderAsync(folder, request, campaign);
            folder.UpdatedAt = DateTime.UtcNow;

            var saved = await _folders.UpdateAsync(folder) ?? throw new InvalidOperationException("Failed to save the folder.");
            await BroadcastFolderAsync(campaign, saved, readersBefore, "updated");
            return saved;
        }

        /// <summary>Its entries and subfolders move up to its parent, so deleting a folder never deletes notes.</summary>
        public async Task<bool> DeleteFolderAsync(string id)
        {
            var folder = await LoadFolderAsync(id);
            var campaign = await Access.EnsureDmAsync(folder.CampaignId);

            foreach (var child in (await _folders.GetByCampaignIdAsync(campaign.Id!)).Where(f => f.ParentId == id))
            {
                child.ParentId = folder.ParentId;
                child.UpdatedAt = DateTime.UtcNow;
                await _folders.UpdateAsync(child);
            }

            foreach (var entry in (await _repository.GetByCampaignIdAsync(campaign.Id!)).Where(e => e.FolderId == id))
            {
                entry.FolderId = folder.ParentId;
                entry.UpdatedAt = DateTime.UtcNow;
                await _repository.UpdateAsync(entry);
            }

            if (!await _folders.DeleteAsync(id)) return false;
            await SendAsync(campaign.Members.Select(m => m.UserId).ToList(), "folder", id, null, campaign.Id!, "deleted");
            return true;
        }

        private async Task ApplyFolderAsync(ChronicleFolder folder, ChronicleFolderRequest request, Campaign campaign)
        {
            Require(Enum.IsDefined(request.Visibility), "Unknown visibility.");

            var name = request.Name?.Trim() ?? string.Empty;
            Require(name.Length is > 0 and <= MaxFolderNameLength, $"The folder name is required ({MaxFolderNameLength} characters at most).");

            var parentId = string.IsNullOrWhiteSpace(request.ParentId) ? null : request.ParentId;
            if (parentId != null)
            {
                await LoadFolderInAsync(parentId, folder.CampaignId!);
                if (folder.Id != null)
                {
                    var byId = (await _folders.GetByCampaignIdAsync(folder.CampaignId!)).ToDictionary(f => f.Id!);
                    for (var id = parentId; id != null; id = byId.GetValueOrDefault(id)?.ParentId)
                        Require(id != folder.Id, "A folder can't go inside itself.");
                }
            }

            folder.Name = name;
            folder.ParentId = parentId;
            folder.Visibility = request.Visibility;
            folder.SharedWith = SharedWithFor(request.Visibility, request.SharedWith, campaign);
        }

        private async Task<ChronicleFolder> LoadFolderAsync(string id)
        {
            Guard.NotNullOrWhiteSpace(id, nameof(id));
            Require(ObjectId.TryParse(id, out _), "Invalid folder id.");
            var folder = await _folders.GetByIdAsync(id);
            return folder == null || folder.IsDeleted ? throw new NotFoundException("Folder not found.") : folder;
        }

        private async Task LoadFolderInAsync(string id, string campaignId)
        {
            var folder = await LoadFolderAsync(id);
            Require(folder.CampaignId == campaignId, "That folder belongs to another campaign.");
        }

        #endregion

        #region Comments

        public async Task<List<ChronicleComment>> GetCommentsAsync(string entryId)
        {
            await EnsureCanReadAsync(await LoadEntryAsync(entryId));
            return await _comments.GetByEntryIdAsync(entryId);
        }

        public async Task<ChronicleComment> AddCommentAsync(string entryId, ChronicleCommentRequest request)
        {
            Guard.NotNull(request, nameof(request));
            var entry = await LoadEntryAsync(entryId);
            var isDm = await EnsureCanReadAsync(entry);
            Require((await SharesAsync(entry.CampaignId!)).IsShared(entry), "Share this entry with players to discuss it.");

            var text = request.Text?.Trim() ?? string.Empty;
            Require(text.Length is > 0 and <= MaxCommentLength, $"A comment is 1 to {MaxCommentLength} characters.");

            var comment = new ChronicleComment
            {
                CampaignId = entry.CampaignId,
                EntryId = entryId,
                UserId = UserId,
                Username = Username,
                IsDm = isDm,
                Text = text,
                CreatedAt = DateTime.UtcNow,
            };

            var created = await _comments.CreateAsync(comment) ?? throw new InvalidOperationException("Failed to post the comment.");
            await BroadcastCommentAsync(entry, created.Id!, "commented");
            return created;
        }

        public async Task<bool> DeleteCommentAsync(string entryId, string commentId)
        {
            var entry = await LoadEntryAsync(entryId);
            var isDm = await EnsureCanReadAsync(entry);

            Require(ObjectId.TryParse(commentId, out _), "Invalid comment id.");
            var comment = await _comments.GetByIdAsync(commentId);
            if (comment == null || comment.IsDeleted || comment.EntryId != entryId) throw new NotFoundException("Comment not found.");
            if (!isDm && comment.UserId != GetCurrentUserId()) throw new ForbiddenException("You can only delete your own comments.");

            if (!await _comments.DeleteAsync(commentId)) return false;
            await BroadcastCommentAsync(entry, commentId, "uncommented");
            return true;
        }

        #endregion

        #region Live updates

        /// <summary>
        /// DMs and every player who could see the entry before or after the change (so an unshare reaches them).
        /// Players who can see it now but couldn't before get "shared" instead, so they can be told.
        /// </summary>
        private async Task BroadcastEntryAsync(Campaign campaign, ChronicleEntry entry, ChronicleEntry? before, string action)
        {
            var shares = await SharesAsync(campaign.Id!);
            bool CanReadBefore(string u) => before != null && shares.CanRead(before, u);
            var members = campaign.Members.Select(m => m.UserId).ToList();
            var gained = action == "deleted"
                ? new List<string>()
                : members.Where(u => !campaign.IsDm(u) && shares.CanRead(entry, u) && !CanReadBefore(u)).ToList();
            var others = members
                .Where(u => !gained.Contains(u) && (campaign.IsDm(u) || shares.CanRead(entry, u) || CanReadBefore(u)))
                .ToList();

            await SendAsync(gained, "entry", entry.Id!, entry.Id!, entry.CampaignId!, "shared");
            await SendAsync(others, "entry", entry.Id!, entry.Id!, entry.CampaignId!, action);
        }

        /// <summary>Players who can now read the folder but couldn't before get "shared"; everyone else refetches.</summary>
        private async Task BroadcastFolderAsync(Campaign campaign, ChronicleFolder folder, HashSet<string> readersBefore, string action)
        {
            var readersAfter = await ReadersAsync(campaign, folder);
            var members = campaign.Members.Select(m => m.UserId).ToList();
            var gained = members.Where(u => readersAfter.Contains(u) && !readersBefore.Contains(u)).ToList();

            await SendAsync(gained, "folder", folder.Id!, null, campaign.Id!, "shared");
            await SendAsync(members.Except(gained).ToList(), "folder", folder.Id!, null, campaign.Id!, action);
        }

        private async Task<HashSet<string>> ReadersAsync(Campaign campaign, ChronicleFolder folder)
        {
            var shares = await SharesAsync(campaign.Id!);
            return campaign.Members.Select(m => m.UserId).Where(u => !campaign.IsDm(u) && shares.CanReadFolder(folder, u)).ToHashSet();
        }

        private async Task BroadcastCommentAsync(ChronicleEntry entry, string commentId, string action)
        {
            var campaign = await Access.GetCampaignAsync(entry.CampaignId);
            if (campaign == null) return;
            var shares = await SharesAsync(campaign.Id!);
            var recipients = campaign.Members.Select(m => m.UserId).Where(u => campaign.IsDm(u) || shares.CanRead(entry, u)).ToList();
            await SendAsync(recipients, "comment", commentId, entry.Id!, entry.CampaignId!, action);
        }

        private async Task SendAsync(List<string> userIds, string kind, string id, string? entryId, string campaignId, string action)
        {
            if (userIds.Count == 0) return;
            await _entitySyncService.BroadcastToUsers(
                "EntityChanged",
                new
                {
                    entityType = "Chronicle",
                    entityId = id,
                    action,
                    data = new { kind, id, entryId, campaignId },
                    changedBy = Username,
                    timestamp = DateTime.UtcNow
                },
                userIds);
        }

        #endregion

        #region Sharing

        private async Task<ShareMap> SharesAsync(string campaignId) => new(await _folders.GetByCampaignIdAsync(campaignId));

        /// <summary>Who can read what: an entry or folder is readable when it, or any folder above it, is shared with the user.</summary>
        internal sealed class ShareMap
        {
            private readonly Dictionary<string, ChronicleFolder> _byId;

            public ShareMap(IEnumerable<ChronicleFolder> folders) =>
                _byId = folders.Where(f => f.Id != null && !f.IsDeleted).ToDictionary(f => f.Id!);

            /// <summary>The folder and every folder above it, nearest first. Stops on broken (looping) data.</summary>
            public IEnumerable<ChronicleFolder> Chain(string? folderId)
            {
                var seen = new HashSet<string>();
                for (var id = folderId; id != null && seen.Add(id) && _byId.TryGetValue(id, out var folder); id = folder.ParentId)
                    yield return folder;
            }

            public bool CanRead(ChronicleEntry entry, string? userId) =>
                entry.IsVisibleTo(userId) || Chain(entry.FolderId).Any(f => f.IsVisibleTo(userId));

            public bool CanReadFolder(ChronicleFolder folder, string? userId) =>
                folder.IsVisibleTo(userId) || Chain(folder.ParentId).Any(f => f.IsVisibleTo(userId));

            public bool IsShared(ChronicleEntry entry) =>
                entry.Visibility != ChronicleVisibility.Private || Chain(entry.FolderId).Any(f => f.Visibility != ChronicleVisibility.Private);
        }

        #endregion

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
