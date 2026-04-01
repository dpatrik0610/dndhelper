using dndhelper.Models.RollModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IRollHistoryService
    {
        Task<RollRecord?> CreateAsync(RollRecord record);
        Task<IReadOnlyList<RollRecord>> GetMyPublicRollsAsync(string userId, int page, int pageSize);
        Task<IReadOnlyList<RollRecord>> GetRollsByCampaignAsync(string campaignId, int page, int pageSize);
    }
}
