using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using MongoDB.Bson;
using System;
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
        private readonly ICharacterRepository _characters;
        private readonly IUserRepository _users;

        public ChatService(IChatRepository repository, ICharacterRepository characters, IUserRepository users)
        {
            _repository = Guard.NotNull(repository, nameof(repository));
            _characters = Guard.NotNull(characters, nameof(characters));
            _users = Guard.NotNull(users, nameof(users));
        }

        public async Task<ChatMessage> SendAsync(ChatMember author, string campaignId, ChatSendRequest request)
        {
            var text = CleanText(request?.Text);
            var name = author.Name;
            var characterId = string.IsNullOrEmpty(request!.CharacterId) ? null : request.CharacterId;
            if (characterId != null)
            {
                // Speak as your own character from this campaign; the DM may speak as any of them.
                var character = ValidId(characterId) ? await _characters.GetByIdAsync(characterId) : null;
                if (character == null || character.IsDeleted || character.CampaignId != campaignId
                    || !(author.IsDm || (character.OwnerIds?.Contains(author.UserId) ?? false)))
                    throw new ArgumentException("You can't speak as that character.");
                name = character.Name ?? name;
            }

            // Players whisper to the DMs; a DM whispers to one player.
            string? toUserId = null, toName = null;
            if (request.Whisper && author.IsDm)
            {
                var to = ValidId(request.ToUserId) ? await _users.GetByIdAsync(request.ToUserId!) : null;
                if (to == null || to.IsDeleted) throw new ArgumentException("Pick who to whisper to.");
                (toUserId, toName) = (to.Id, to.Username);
            }

            var message = new ChatMessage
            {
                CampaignId = campaignId,
                UserId = author.UserId,
                CharacterId = characterId,
                Name = name,
                IsDm = author.IsDm,
                Text = text,
                Whisper = request.Whisper,
                ToUserId = toUserId,
                ToName = toName,
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
