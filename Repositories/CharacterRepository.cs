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
            : base(logger, cache, context, "Characters") { _logger = logger; }

        public async Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId)
        {
            var filter = Builders<Character>.Filter.AnyEq(c => c.OwnerIds, ownerId);
            var characters = await _collection.Find(filter).ToListAsync();
            if (characters.Any())
                _logger.Information($"Character retrieved for {ownerId}.");
            return characters;
        }
    }
}
