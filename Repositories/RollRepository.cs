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
            : base(logger, cache, context, "Rolls") { }

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
