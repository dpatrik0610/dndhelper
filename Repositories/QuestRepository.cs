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
    public class QuestRepository : MongoRepository<Quest>, IQuestRepository
    {
        public QuestRepository(ILogger logger, IMemoryCache cache, MongoDbContext context)
            : base(logger, cache, context, "Quests")
        {
        }

        public async Task<IEnumerable<Quest>> GetByCampaignIdAsync(string campaignId)
        {
            var filter = Builders<Quest>.Filter.Eq(q => q.CampaignId, campaignId)
                & Builders<Quest>.Filter.Eq(q => q.IsDeleted, false);

            return await _collection.Find(filter).ToListAsync();
        }
    }
}
