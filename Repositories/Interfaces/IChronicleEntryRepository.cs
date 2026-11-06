using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IChronicleEntryRepository : IRepository<ChronicleEntry>
    {
        Task<List<ChronicleEntry>> GetByCampaignIdAsync(string campaignId);
    }
}
