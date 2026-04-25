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
    public class EncounterRepository : MongoRepository<Encounter>, IEncounterRepository
    {
        public EncounterRepository(ILogger logger, IMemoryCache cache, MongoDbContext context)
            : base(logger, cache, context, "Encounters")
        {
        }

        public async Task<IEnumerable<Encounter>> GetByCampaignIdAsync(string campaignId)
        {
            var filter = Builders<Encounter>.Filter.Eq(e => e.CampaignId, campaignId)
                & Builders<Encounter>.Filter.Eq(e => e.IsDeleted, false);

            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<IEnumerable<Encounter>> GetBySessionIdAsync(string sessionId)
        {
            var filter = Builders<Encounter>.Filter.Eq(e => e.SessionId, sessionId)
                & Builders<Encounter>.Filter.Eq(e => e.IsDeleted, false);

            return await _collection.Find(filter).ToListAsync();
        }
    }
}
