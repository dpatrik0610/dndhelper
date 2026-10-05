using dndhelper.Models;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    /// <summary>
    /// Campaign chat, independent of how it's delivered. Callers check campaign access; this checks the message and who may touch it.
    /// Invalid input throws ArgumentException with a user-facing message. Callers deliver a message only to members it's VisibleTo.
    /// </summary>
    public interface IChatService
    {
        Task<ChatMessage> SendAsync(ChatMember author, string campaignId, ChatSendRequest request);
        /// <summary>Authors only.</summary>
        Task<ChatMessage> EditAsync(ChatMember author, string campaignId, string messageId, string text);
        /// <summary>The author or a DM. Returns the removed message so callers know who saw it.</summary>
        Task<ChatMessage> DeleteAsync(ChatMember member, string campaignId, string messageId);
        /// <summary>The newest page, or the page before `beforeId`.</summary>
        Task<ChatPage> PageAsync(ChatMember viewer, string campaignId, string? beforeId);
    }
}
