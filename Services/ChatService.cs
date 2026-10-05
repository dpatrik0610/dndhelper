using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class ChatService : IChatService
    {
        public const int MaxLength = 2000;
        private const int PageSize = 100;

        // Control characters (bar newline and tab) and bidi overrides/isolates, which can make text read differently than it is.
        private static readonly Regex Invisible = new(@"[\p{Cc}\u202A-\u202E\u2066-\u2069-[\n\t]]", RegexOptions.Compiled);
        private static readonly Regex BlankRun = new(@"\n[^\S\n]*(\n[^\S\n]*){2,}", RegexOptions.Compiled);

        private readonly IChatRepository _repository;
        private readonly ICampaignRepository _campaigns;
        private readonly ICharacterRepository _characters;

        public ChatService(IChatRepository repository, ICampaignRepository campaigns, ICharacterRepository characters)
        {
            _repository = Guard.NotNull(repository, nameof(repository));
            _campaigns = Guard.NotNull(campaigns, nameof(campaigns));
            _characters = Guard.NotNull(characters, nameof(characters));
        }

        public async Task<List<ChatCampaign>> CampaignsAsync(ChatCaller caller)
        {
            var playing = (await _characters.GetByOwnerIdAsync(caller.UserId))
                .Where(c => !c.IsDeleted && c.Id != null)
                .Select(c => c.Id!)
                .ToHashSet();

            // ponytail: scans every campaign; add an owner/character index query if campaigns run into the thousands.
            var campaigns = await _campaigns.GetAllAsync();
            return campaigns
                .Where(c => IsOwner(c, caller.UserId) || (c.CharacterIds?.Any(playing.Contains) ?? false))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new ChatCampaign(c.Id, c.Name, IsDm(c, caller)))
                .ToList();
        }

        public async Task<(ChatMember Member, ChatRoom Room)> OpenAsync(ChatCaller caller, string campaignId)
        {
            var campaign = await LoadCampaign(campaignId);
            var characters = await CampaignCharacters(campaign);
            var isDm = IsDm(campaign, caller);
            var mine = characters.Where(c => Owns(c, caller.UserId)).ToList();
            if (!isDm && mine.Count == 0) throw new ArgumentException("You're not in this campaign.");

            var member = new ChatMember(caller.UserId, caller.Name, isDm);
            var room = new ChatRoom(
                campaign.Id,
                campaign.Name,
                isDm,
                await PageAsync(member, campaign.Id, null),
                isDm ? WhisperTargets(campaign, characters).Select(c => new ChatTarget(c.Id!, c.Name ?? "Unnamed")).ToList() : new List<ChatTarget>(),
                isDm ? new List<ChatSpeaker>() : mine.Select(c => new ChatSpeaker(c.Id!, c.Name ?? "Unnamed")).ToList(),
                characters.Where(c => !string.IsNullOrWhiteSpace(c.ImageUrl)).ToDictionary(c => c.Id!, c => c.ImageUrl!));
            return (member, room);
        }

        public async Task<ChatMessage> SendAsync(ChatMember author, string campaignId, ChatSendRequest request)
        {
            var text = CleanText(request?.Text);
            var campaign = await LoadCampaign(campaignId);
            var characters = await CampaignCharacters(campaign);

            var name = author.Name;
            var characterId = string.IsNullOrEmpty(request!.CharacterId) ? null : request.CharacterId;
            if (characterId != null)
            {
                // Speak as your own character from this campaign; the DM may speak as any of them.
                var character = characters.FirstOrDefault(c => c.Id == characterId);
                if (character == null || !(author.IsDm || Owns(character, author.UserId)))
                    throw new ArgumentException("You can't speak as that character.");
                name = character.Name ?? name;
            }

            // Players whisper to the DMs; a DM whispers to one of the campaign's player characters (all its owners read it).
            Character? to = null;
            if (request.Whisper && author.IsDm)
                to = WhisperTargets(campaign, characters).FirstOrDefault(c => c.Id == request.ToCharacterId)
                    ?? throw new ArgumentException("Pick a character to whisper to.");

            var message = new ChatMessage
            {
                CampaignId = campaignId,
                UserId = author.UserId,
                CharacterId = characterId,
                Name = name,
                IsDm = author.IsDm,
                Text = text,
                Whisper = request.Whisper,
                ToCharacterId = to?.Id,
                ToUserIds = to == null ? new List<string>() : PlayerOwners(campaign, to),
                ToName = to?.Name,
                CreatedAt = DateTime.UtcNow,
            };
            await _repository.AddAsync(message);
            return message;
        }

        public async Task<ChatMessage> EditAsync(ChatMember author, string campaignId, string messageId, string text)
        {
            var message = await Find(campaignId, messageId);
            if (message.UserId != author.UserId) throw new ArgumentException("You can only edit your own messages.");

            message.Text = CleanText(text);
            message.EditedAt = message.UpdatedAt = DateTime.UtcNow;
            await _repository.ReplaceAsync(message);
            return message;
        }

        public async Task<ChatMessage> DeleteAsync(ChatMember member, string campaignId, string messageId)
        {
            var message = await Find(campaignId, messageId);
            if (message.UserId != member.UserId && !member.IsDm) throw new ArgumentException("You can only delete your own messages.");

            message.IsDeleted = true;
            message.UpdatedAt = DateTime.UtcNow;
            await _repository.ReplaceAsync(message);
            return message;
        }

        public async Task<ChatPage> PageAsync(ChatMember viewer, string campaignId, string? beforeId)
        {
            if (beforeId != null && !ValidId(beforeId)) throw new ArgumentException("Invalid message.");

            // One extra tells whether there's an older page.
            var newestFirst = await _repository.PageAsync(campaignId, viewer, beforeId, PageSize + 1);
            var hasMore = newestFirst.Count > PageSize;
            if (hasMore) newestFirst.RemoveAt(PageSize);
            newestFirst.Reverse();
            return new ChatPage(newestFirst, hasMore);
        }

        private async Task<Campaign> LoadCampaign(string campaignId)
        {
            var campaign = ValidId(campaignId) ? await _campaigns.GetByIdAsync(campaignId) : null;
            if (campaign == null || campaign.IsDeleted) throw new ArgumentException("Campaign not found.");
            return campaign;
        }

        /// <summary>The campaign's character list is the membership list.</summary>
        private async Task<List<Character>> CampaignCharacters(Campaign campaign) =>
            (await _characters.GetByIdsAsync(campaign.CharacterIds ?? new List<string>()))
                .Where(c => !c.IsDeleted && c.Id != null)
                .ToList();

        /// <summary>A character's owners who aren't the campaign's DMs.</summary>
        private static List<string> PlayerOwners(Campaign campaign, Character character) =>
            (character.OwnerIds ?? new List<string>()).Where(owner => !IsOwner(campaign, owner)).Distinct().ToList();

        /// <summary>Characters a DM can whisper to: those with at least one player owner, by name.</summary>
        private static List<Character> WhisperTargets(Campaign campaign, List<Character> characters) =>
            characters
                .Where(c => PlayerOwners(campaign, c).Count > 0)
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static bool IsOwner(Campaign campaign, string userId) => campaign.OwnerIds?.Contains(userId) ?? false;
        private static bool IsDm(Campaign campaign, ChatCaller caller) => caller.IsAdmin || IsOwner(campaign, caller.UserId);
        private static bool Owns(Character character, string userId) => character.OwnerIds?.Contains(userId) ?? false;

        private async Task<ChatMessage> Find(string campaignId, string messageId)
        {
            var message = ValidId(messageId) ? await _repository.GetAsync(messageId) : null;
            if (message == null || message.CampaignId != campaignId) throw new ArgumentException("That message is gone.");
            return message;
        }

        /// <summary>
        /// Text is stored and shown as plain text (never HTML), so only invisible tricks need removing:
        /// control and bidi characters, and walls of blank lines.
        /// </summary>
        public static string CleanText(string? text)
        {
            var cleaned = Invisible.Replace((text ?? string.Empty).Replace("\r\n", "\n"), string.Empty);
            var trimmed = BlankRun.Replace(cleaned, "\n\n").Trim();
            if (trimmed.Length == 0) throw new ArgumentException("Write something first.");
            if (trimmed.Length > MaxLength) throw new ArgumentException($"Keep it under {MaxLength} characters.");
            return trimmed;
        }

        private static bool ValidId(string? id) => ObjectId.TryParse(id, out _);
    }
}
