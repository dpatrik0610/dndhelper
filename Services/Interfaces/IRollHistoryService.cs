using dndhelper.Models.RollModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IRollHistoryService
    {
        Task<RollRecord?> RecordAsync(RollRecord record);
        Task<IReadOnlyList<RollRecord>> GetHistoryAsync(string userId, string? campaignId, bool isDm, RollType? type, int page, int pageSize);
    }
}
