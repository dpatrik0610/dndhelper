using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class MonsterRepository : MongoRepository<Monster>, IMonsterRepository
    {
        // Case-insensitive equality, so "goblin" finds "Goblin".
        private static readonly Collation IgnoreCase = new("en", strength: CollationStrength.Secondary);

        public MonsterRepository(MongoDbContext context, IMemoryCache cache, ILogger logger)
            : base(logger, cache, context, "Monsters") { }

        private static FilterDefinition<Monster> NotDeleted => Builders<Monster>.Filter.Ne(m => m.IsDeleted, true);

        public async Task<(List<Monster> Items, long Total)> SearchAsync(FilterDefinition<Monster> scope, MonsterSearchCriteria c)
        {
            var f = Builders<Monster>.Filter;
            var filter = NotDeleted & scope;

            if (!string.IsNullOrWhiteSpace(c.Query))
            {
                var rx = new BsonRegularExpression(Regex.Escape(c.Query.Trim()), "i");
                filter &= f.Regex(m => m.Name, rx) | f.Regex(m => m.Type, rx) | f.Regex("Tags", rx);
            }
            if (!string.IsNullOrWhiteSpace(c.Type))
                filter &= f.Regex(m => m.Type, new BsonRegularExpression($"^{Regex.Escape(c.Type.Trim())}$", "i"));
            if (c.MinCr.HasValue) filter &= f.Gte(m => m.ChallengeRating, c.MinCr.Value);
            if (c.MaxCr.HasValue) filter &= f.Lte(m => m.ChallengeRating, c.MaxCr.Value);
            if (c.IsNpc.HasValue) filter &= f.Eq(m => m.IsNpc, c.IsNpc.Value);

            var s = Builders<Monster>.Sort;
            var sort = c.SortBy?.ToLowerInvariant() == "cr"
                ? (c.Desc ? s.Descending(m => m.ChallengeRating) : s.Ascending(m => m.ChallengeRating)).Ascending(m => m.Name)
                : c.Desc ? s.Descending(m => m.Name) : s.Ascending(m => m.Name);

            var page = c.Page < 1 ? 1 : c.Page;
            var pageSize = c.PageSize is < 1 or > 100 ? 25 : c.PageSize;

            var total = await _collection.CountDocumentsAsync(filter);
            var items = await _collection.Find(filter).Sort(sort).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
            return (items, total);
        }

        public async Task<List<string>> GetTypesAsync(FilterDefinition<Monster> scope)
        {
            var types = await _collection.Distinct(m => m.Type, NotDeleted & scope).ToListAsync();
            return types.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).OrderBy(t => t).ToList();
        }

        public Task<List<Monster>> FindByNamesAsync(IEnumerable<string> names) =>
            _collection.Find(NotDeleted & Builders<Monster>.Filter.In(m => m.Name, names), new FindOptions { Collation = IgnoreCase })
                .ToListAsync();
    }
}
