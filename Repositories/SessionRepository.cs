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
    public class SessionRepository : MongoRepository<Session>, ISessionRepository
    {
        public SessionRepository(ILogger logger, IMemoryCache cache, MongoDbContext context)
            : base(logger, cache, context, "Sessions")
        {
        }

        public async Task<IEnumerable<Session>> GetByCampaignIdAsync(string campaignId)
        {
            var filter = Builders<Session>.Filter.Eq(s => s.CampaignId, campaignId);
            return await _collection.Find(filter).ToListAsync();
        }
    }
}
