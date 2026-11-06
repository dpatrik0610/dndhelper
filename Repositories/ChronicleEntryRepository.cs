using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Serilog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class ChronicleEntryRepository : MongoRepository<ChronicleEntry>, IChronicleEntryRepository
    {
        public ChronicleEntryRepository(ILogger logger, IMemoryCache cache, MongoDbContext context)
            : base(logger, cache, context, "ChronicleEntries")
        {
        }

        public async Task<List<ChronicleEntry>> GetByCampaignIdAsync(string campaignId) =>
            await _collection.Find(x => x.CampaignId == campaignId && !x.IsDeleted).SortBy(x => x.Title).ToListAsync();
    }
}
