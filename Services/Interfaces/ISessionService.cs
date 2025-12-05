using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ISessionService : IBaseService<Session>, IInternalBaseService<Session>
    {
        Task<Session?> CreateAndNotifyAsync(Session session);
        Task<Session?> UpdateAndNotifyAsync(string id, Session session);
        Task<bool> DeleteAndNotifyAsync(string id);
        Task<IEnumerable<Session>> GetByCampaignIdAsync(string campaignId);
    }
}
