using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IQuestService : IBaseService<Quest>, IInternalBaseService<Quest>
    {
        Task<IEnumerable<Quest>> GetByCampaignIdAsync(string campaignId);
        Task<Quest?> CreateAndNotifyAsync(Quest quest);
        Task<Quest?> UpdateAndNotifyAsync(string id, Quest quest);
        Task<bool> DeleteAndNotifyAsync(string id);

        // Atomic Quest Objectives Management
        Task<Quest?> AddObjectiveAsync(string questId, QuestObjective objective);
        Task<Quest?> UpdateObjectiveAsync(string questId, QuestObjective objective);
        Task<Quest?> DeleteObjectiveAsync(string questId, string objectiveId);
    }
}
