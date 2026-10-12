using dndhelper.Database;
using dndhelper.Models.CharacterModels;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class CharacterRepository : MongoRepository<Character>, ICharacterRepository
    {
        private readonly ILogger _logger;
        public CharacterRepository(MongoDbContext context, ILogger logger, IMemoryCache cache)
            : base(logger, cache, context, "Characters")
        {
            _logger = logger;
            if (_indexesCreated) return;
            TryCreateIndexes(new CreateIndexModel<Character>(
                Builders<Character>.IndexKeys.Ascending(c => c.OwnerIds),
                new CreateIndexOptions { Name = "idx_characters_ownerIds" }));
            _indexesCreated = true;
        }

        private static bool _indexesCreated;

        public async Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId)
        {
            var filter = Builders<Character>.Filter.AnyEq(c => c.OwnerIds, ownerId);
            var characters = await _collection.Find(filter).ToListAsync();
            if (characters.Any())
                _logger.Debug("Character retrieved for {OwnerId}.", ownerId);
            return characters;
        }
    }
}
