using dndhelper.Database;
using dndhelper.Models.RollModels;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace dndhelper.Repositories
{
    public class RollRepository : MongoRepository<RollRecord>, IRollRepository
    {
        public RollRepository(MongoDbContext context, ILogger logger, IMemoryCache cache)
            : base(logger, cache, context, "Rolls") { }
    }
}
