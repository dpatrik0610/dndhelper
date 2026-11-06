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
    public class ChronicleCommentRepository : MongoRepository<ChronicleComment>, IChronicleCommentRepository
    {
        private static bool _indexesCreated;

        public ChronicleCommentRepository(ILogger logger, IMemoryCache cache, MongoDbContext context)
            : base(logger, cache, context, "ChronicleComments")
        {
            if (_indexesCreated) return;
            TryCreateIndexes(new CreateIndexModel<ChronicleComment>(
                Builders<ChronicleComment>.IndexKeys.Ascending(x => x.EntryId),
                new CreateIndexOptions { Name = "idx_chronicleComments_entryId" }));
            _indexesCreated = true;
        }

        public async Task<List<ChronicleComment>> GetByEntryIdAsync(string entryId) =>
            await _collection.Find(x => x.EntryId == entryId && !x.IsDeleted).SortBy(x => x.CreatedAt).ToListAsync();

        public async Task DeleteByEntryIdAsync(string entryId)
        {
            foreach (var comment in await GetByEntryIdAsync(entryId))
                RemoveFromCache(comment.Id!);
            await _collection.DeleteManyAsync(x => x.EntryId == entryId);
        }
    }
}
