using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class MonsterRepository : MongoRepository<Monster>, IMonsterRepository
    {
        public MonsterRepository(MongoDbContext context, IMemoryCache cache, ILogger logger)
            : base(logger, cache, context, "Monsters") { }

        public async Task<List<Monster>> FindByNamePhraseAsync(string namePhrase)
        {
            if (string.IsNullOrWhiteSpace(namePhrase))
                throw new ArgumentException("Monster name phrase is null or empty.");

            var filter = Builders<Monster>.Filter.Regex(m => m.Name,
                new BsonRegularExpression(namePhrase, "i"));

            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<List<Monster>> GetPagedAsync(int page, int pageSize)
        {
            return await _collection.Find(m => !m.IsDeleted)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();
        }

        public async Task<List<Monster>> SearchAsync(string query, int page, int pageSize)
        {
            var filter = Builders<Monster>.Filter.Regex(m => m.Name, new BsonRegularExpression(query, "i"));
            return await _collection.Find(filter)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();
        }

        public async Task<List<Monster>> FindByOwnerIdAsync(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
                throw new ArgumentException("Owner ID cannot be null or empty.");

            var filter = Builders<Monster>.Filter.Eq(m => m.CreatedByUserId, ownerId);
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<List<Monster>> SearchAsync(MonsterSearchCriteria criteria)
        {
            var filterBuilder = Builders<Monster>.Filter;
            var filter = filterBuilder.Eq(m => m.IsDeleted, false);

            _logger.Information("Starting monster search with criteria {@Criteria}", criteria);

            if (!string.IsNullOrWhiteSpace(criteria.Name))
                filter &= filterBuilder.Regex(m => m.Name, new BsonRegularExpression(criteria.Name, "i"));

            if (!string.IsNullOrWhiteSpace(criteria.Type))
            {
                var typeFilters = new List<FilterDefinition<Monster>>
                {
                    filterBuilder.Regex("Type", new BsonRegularExpression(criteria.Type, "i")),
                    filterBuilder.Regex("Type.Type", new BsonRegularExpression(criteria.Type, "i"))
                };
                filter &= filterBuilder.Or(typeFilters);
            }

            if (criteria.Tags?.Any() == true)
                filter &= filterBuilder.AnyIn(m => m.Type!.Tags, criteria.Tags);

            if (criteria.MinCR.HasValue)
                filter &= filterBuilder.Gte(m => m.CR, criteria.MinCR.Value);

            if (criteria.MaxCR.HasValue)
                filter &= filterBuilder.Lte(m => m.CR, criteria.MaxCR.Value);

            var sort = criteria.SortDescending
                ? Builders<Monster>.Sort.Descending(criteria.SortBy)
                : Builders<Monster>.Sort.Ascending(criteria.SortBy);

            var monsters = await _collection
                .Find(filter)
                .Sort(sort)
                .Skip((criteria.Page - 1) * criteria.PageSize)
                .Limit(criteria.PageSize)
                .ToListAsync();

            _logger.Information("Completed monster search, returning {Count} results", monsters.Count);
            return monsters;
        }
    }
}
