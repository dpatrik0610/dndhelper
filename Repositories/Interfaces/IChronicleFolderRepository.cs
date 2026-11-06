using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IChronicleFolderRepository : IRepository<ChronicleFolder>
    {
        Task<List<ChronicleFolder>> GetByCampaignIdAsync(string campaignId);
    }
}
