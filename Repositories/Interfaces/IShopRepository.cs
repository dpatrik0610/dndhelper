using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IShopRepository : IRepository<Shop>
    {
        Task<IEnumerable<Shop>> GetByCampaignIdAsync(string campaignId);
    }
}
