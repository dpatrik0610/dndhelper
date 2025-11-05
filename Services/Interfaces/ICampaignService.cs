using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ICampaignService : IBaseService<Campaign>
    {
        Task<Campaign> CreateAsync(Campaign campaign, string userId);
        Task<bool> DeleteAsync(string id, string userId);

        Task<List<Character>> GetCharactersAsync(string campaignId);
        Task<Campaign?> AddCharacterAsync(string campaignId, string characterId);
        Task<Campaign?> RemoveCharacterAsync(string campaignId, string characterId);

        Task<Campaign?> AddWorldAsync(string campaignId, string worldId);
        Task<Campaign?> RemoveWorldAsync(string campaignId, string worldId);

        Task<Campaign?> AddQuestAsync(string campaignId, string questId);
        Task<Campaign?> RemoveQuestAsync(string campaignId, string questId);

        //Task<List<string>> GetNotesAsync(string campaignId);
        Task<Campaign?> AddNoteAsync(string campaignId, string noteId);
        Task<Campaign?> RemoveNoteAsync(string campaignId, string noteId);

        Task<Campaign?> AddSessionAsync(string campaignId, string sessionId);
        Task<Campaign?> RemoveSessionAsync(string campaignId, string sessionId);
        Task<Campaign?> SetCurrentSessionAsync(string campaignId, string sessionId);
    }
}
