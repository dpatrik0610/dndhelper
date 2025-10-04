using dndhelper.Database;
using dndhelper.Models.CharacterModels;
using dndhelper.Repositories.Interfaces;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using Microsoft.Extensions.Caching.Memory;
namespace dndhelper.Repositories
{
    public class CharacterRepository : MongoRepository<Character>, ICharacterRepository
    {
        public CharacterRepository(MongoDbContext context, ILogger logger, IMemoryCache cache)
            : base(logger, cache, context, "Characters") { }

        public async Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId)
        {
            var result = await _collection.FindAsync(c => c.OwnerId == ownerId);
            return await result.ToListAsync();
        }
    }
}
