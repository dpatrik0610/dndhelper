using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace dndhelper.Repositories
{
    public class StoredFileRepository : MongoRepository<StoredFile>, IStoredFileRepository
    {
        public StoredFileRepository(ILogger logger, IMemoryCache cache, MongoDbContext context)
            : base(logger, cache, context, "StoredFiles")
        {
        }
    }
}
