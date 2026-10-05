using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    /// <summary>
    /// Campaign chat, independent of how it's delivered. Membership comes from OpenAsync: the campaign's DMs (and admins)
    /// and the owners of its characters. Invalid input throws ArgumentException with a user-facing message.
    /// Callers deliver a message only to members it's VisibleTo.
    /// </summary>
    public interface IChatService
    {
        /// <summary>Campaigns the caller can chat in, by name.</summary>
        Task<List<ChatCampaign>> CampaignsAsync(ChatCaller caller);
        /// <summary>Checks membership; the member is what the other calls take.</summary>
        Task<(ChatMember Member, ChatRoom Room)> OpenAsync(ChatCaller caller, string campaignId);
        Task<ChatMessage> SendAsync(ChatMember author, string campaignId, ChatSendRequest request);
        /// <summary>Authors only.</summary>
        Task<ChatMessage> EditAsync(ChatMember author, string campaignId, string messageId, string text);
        /// <summary>The author or a DM. Returns the removed message so callers know who saw it.</summary>
        Task<ChatMessage> DeleteAsync(ChatMember member, string campaignId, string messageId);
        /// <summary>The newest page, or the page before `beforeId`.</summary>
        Task<ChatPage> PageAsync(ChatMember viewer, string campaignId, string? beforeId);
    }
}
