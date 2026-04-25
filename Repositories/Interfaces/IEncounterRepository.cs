using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IEncounterRepository : IRepository<Encounter>
    {
        Task<IEnumerable<Encounter>> GetByCampaignIdAsync(string campaignId);
        Task<IEnumerable<Encounter>> GetBySessionIdAsync(string sessionId);
    }
}
