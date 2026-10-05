using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IChatRepository
    {
        Task AddAsync(ChatMessage message);
        Task<ChatMessage?> GetAsync(string id);
        Task ReplaceAsync(ChatMessage message);
        /// <summary>Up to `limit` messages the viewer may see, older than `beforeId` when given; newest first.</summary>
        Task<List<ChatMessage>> PageAsync(string campaignId, ChatMember viewer, string? beforeId, int limit);
    }
}
