using dndhelper.Database;
using dndhelper.Models.RollModels;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Serilog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class RollRepository : MongoRepository<RollRecord>, IRollRepository
    {
        public RollRepository(MongoDbContext context, ILogger logger, IMemoryCache cache)
            : base(logger, cache, context, "Rolls")
        {
            if (_indexesCreated) return;
            // Match RollHistoryService's two queries, newest first.
            TryCreateIndexes(
                new CreateIndexModel<RollRecord>(
                    Builders<RollRecord>.IndexKeys.Ascending(r => r.UserId).Ascending(r => r.Type).Descending(r => r.CreatedAt),
                    new CreateIndexOptions { Name = "idx_rolls_user_type_createdAt" }),
                new CreateIndexModel<RollRecord>(
                    Builders<RollRecord>.IndexKeys.Ascending(r => r.CampaignId).Descending(r => r.CreatedAt),
                    new CreateIndexOptions { Name = "idx_rolls_campaign_createdAt" }));
            _indexesCreated = true;
        }

        private static bool _indexesCreated;

        public async Task<List<RollRecord>> QueryAsync(FilterDefinition<RollRecord> filter, int skip, int limit)
        {
            var query = _collection
                .Find(filter)
                .SortByDescending(r => r.CreatedAt)
                .Skip(skip)
                .Limit(limit);

            return await query.ToListAsync();
        }
    }
}
