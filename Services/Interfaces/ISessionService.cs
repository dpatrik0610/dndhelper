using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ISessionService : IBaseService<Session>, IInternalBaseService<Session>
    {
        Task<List<Session>> GetByCampaignIdAsync(string campaignId);
        Task<Session> GetVisibleAsync(string id);
        Task<Session> CreateAndNotifyAsync(Session input);
        Task<Session> UpdateDetailsAsync(string id, Session input);
        Task<Session> StartAsync(string id);
        Task<Session> EndAsync(string id);
        Task<bool> DeleteAndNotifyAsync(string id);
        Task<Session> AddNoteAsync(string id, SessionNoteInput input);
        Task<Session> UpdateNoteAsync(string id, string noteId, SessionNoteInput input);
        Task<Session> DeleteNoteAsync(string id, string noteId);
    }
}
