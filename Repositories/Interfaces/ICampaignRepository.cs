using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface ICampaignRepository : IRepository<Campaign>
    {
        Task<List<Campaign>> GetForMemberAsync(string userId);
        Task<Campaign?> GetByInviteCodeAsync(string code);
    }
}
