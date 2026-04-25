using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IEncounterService : IBaseService<Encounter>, IInternalBaseService<Encounter>
    {
        Task<Encounter?> CreateAndNotifyAsync(Encounter encounter);
        Task<Encounter?> UpdateAndNotifyAsync(string id, Encounter encounter);
        Task<bool> DeleteAndNotifyAsync(string id);
        Task<IEnumerable<Encounter>> GetByCampaignIdAsync(string campaignId);
        Task<IEnumerable<Encounter>> GetBySessionIdAsync(string sessionId);
    }
}
