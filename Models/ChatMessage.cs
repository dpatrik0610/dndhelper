using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace dndhelper.Models
{
    /// <summary>
    /// One line of a campaign's chat. Not tied to the tabletop, so any campaign screen can show the same conversation.
    /// Names and IsDm are captured when sent, so old messages keep reading the same after renames.
    /// </summary>
    public class ChatMessage : IEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string CampaignId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;

        /// <summary>The character speaking; null = the user themselves (the DM, usually).</summary>
        [BsonRepresentation(BsonType.ObjectId)]
        public string? CharacterId { get; set; }

        /// <summary>Character name, or the user name when no character speaks.</summary>
        public string Name { get; set; } = string.Empty;
        public bool IsDm { get; set; }
        public string Text { get; set; } = string.Empty;

        /// <summary>Only the sender, the DMs and the target character's owners see it.</summary>
        public bool Whisper { get; set; }
        /// <summary>The character a DM whispers to; null on a player's whisper, which goes to the DMs.</summary>
        [BsonRepresentation(BsonType.ObjectId)]
        public string? ToCharacterId { get; set; }
        /// <summary>That character's owners when it was sent, so a later owner change doesn't reveal or hide it.</summary>
        public List<string> ToUserIds { get; set; } = new();
        public string? ToName { get; set; }
        /// <summary>Legacy: whispers sent to a person, before they went to characters. Still honoured for reading.</summary>
        [BsonRepresentation(BsonType.ObjectId)]
        public string? ToUserId { get; set; }

        public DateTime? EditedAt { get; set; }

        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; }

        /// <summary>Mirrored by the history query in ChatRepository.</summary>
        public bool VisibleTo(ChatMember viewer) =>
            !Whisper || viewer.IsDm || UserId == viewer.UserId || ToUserId == viewer.UserId || (ToUserIds?.Contains(viewer.UserId) ?? false);
    }

    /// <summary>The signed-in user, before campaign membership is checked.</summary>
    public record ChatCaller(string UserId, string Name, bool IsAdmin);

    /// <summary>A member of one campaign's chat: its DM (or an admin), or a player with a character in it.</summary>
    public record ChatMember(string UserId, string Name, bool IsDm);

    /// <summary>A campaign the caller can chat in.</summary>
    public record ChatCampaign(string Id, string Name, bool IsDm);

    /// <summary>A player character the DM can whisper to; all its owners read the whisper.</summary>
    public record ChatTarget(string CharacterId, string Name);

    /// <summary>A character the member can speak as.</summary>
    public record ChatSpeaker(string Id, string Name);

    /// <summary>Everything a chat screen needs when it opens a campaign's chat.</summary>
    public record ChatRoom(
        string CampaignId,
        string CampaignName,
        bool IsDm,
        ChatPage Page,
        List<ChatTarget> WhisperTargets,
        List<ChatSpeaker> Characters,
        // Character id → portrait URL for the campaign characters that have one; looked up live, so new portraits show on old messages.
        Dictionary<string, string> Avatars);

    /// <summary>ToCharacterId is for DM whispers only; a player's whisper always goes to the DMs.</summary>
    public record ChatSendRequest(string Text, string? CharacterId, bool Whisper, string? ToCharacterId);

    /// <summary>Messages oldest first, and whether older ones exist.</summary>
    public record ChatPage(List<ChatMessage> Messages, bool HasMore);
}
