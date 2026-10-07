using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IPollRepository : IRepository<Poll>
    {
        Task<List<Poll>> GetByCampaignIdAsync(string campaignId);

        /// <summary>Reads straight from the database, bypassing the cache (for read-modify-write).</summary>
        Task<Poll?> GetFreshAsync(string id);

        /// <summary>Replaces the poll only if nobody wrote it since <paramref name="expectedVersion"/>.</summary>
        Task<bool> TryReplaceAsync(Poll poll, int expectedVersion);
    }
}
