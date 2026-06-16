using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface ISellRequestRepository : IRepository<SellRequest>
    {
        Task<IEnumerable<SellRequest>> GetByCampaignIdAsync(string campaignId);
        
        /// <summary>
        /// Perform an atomic state transition on a sell request.
        /// </summary>
        Task<SellRequest?> TryUpdateStatusAsync(string id, SellRequestStatus currentStatus, SellRequestStatus newStatus);
    }
}
