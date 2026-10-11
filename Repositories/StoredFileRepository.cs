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
    public class StoredFileRepository : MongoRepository<StoredFile>, IStoredFileRepository
    {
        public StoredFileRepository(ILogger logger, IMemoryCache cache, MongoDbContext context)
            : base(logger, cache, context, "StoredFiles")
        {
        }

        public Task<long> CountAsync(string ownerId, StorageFolder folder) =>
            _collection.CountDocumentsAsync(f => f.OwnerId == ownerId && f.Folder == folder && !f.IsDeleted);

        public Task<List<StoredFile>> ListAsync(string ownerId, StorageFolder folder) =>
            _collection.Find(f => f.OwnerId == ownerId && f.Folder == folder && !f.IsDeleted)
                .SortByDescending(f => f.CreatedAt)
                .ToListAsync();
    }
}
