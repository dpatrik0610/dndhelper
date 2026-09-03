using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IQuestRepository : IRepository<Quest>
    {
        Task<IEnumerable<Quest>> GetByCampaignIdAsync(string campaignId);
    }
}
