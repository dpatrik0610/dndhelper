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
    public class SpellRepository : MongoRepository<Spell>, ISpellRepository
    {
        public SpellRepository(ILogger logger, IMemoryCache cache, MongoDbContext context) : base(logger, cache, context, "Spells")
        {
        }

        public async Task<List<SpellNameResponse>> GetAllNamesAsync(FilterDefinition<Spell> scope)
        {
            var projection = Builders<Spell>.Projection
                .Include(s => s.Id)
                .Include(s => s.Name)
                .Include(s => s.Level)
                .Include(s => s.CampaignId)
                .Include(s => s.School!.Name!);

            var spellsCursor = await _collection.Find(Builders<Spell>.Filter.Ne(s => s.IsDeleted, true) & scope)
                                               .Project<SpellNameResponse>(projection)
                                               .ToListAsync();

            return spellsCursor;
        }
    }
}
