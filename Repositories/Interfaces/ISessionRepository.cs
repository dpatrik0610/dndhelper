using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface ISessionRepository : IRepository<Session>
    {
        Task<List<Session>> GetByCampaignIdAsync(string campaignId);
        Task<Session> ReplaceAsync(Session session);
    }
}
