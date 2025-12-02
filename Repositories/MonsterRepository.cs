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

            var filter = Builders<Monster>.Filter.Regex(
                m => m.Name,
                new BsonRegularExpression(namePhrase, "i"));

            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<List<Monster>> GetPagedAsync(int page, int pageSize)
        {
            if (page <= 0 || pageSize <= 0)
                throw new ArgumentException("Page and page size must be greater than zero.");

            var f = Builders<Monster>.Filter;
            var filter = f.Ne(m => m.IsDeleted, true);

            return await _collection.Find(filter)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();
        }

        public Task<long> GetCountAsync()
        {
            return _collection.CountDocumentsAsync(_ => true);
        }

        public async Task<List<Monster>> SearchAsync(string query, int page, int pageSize)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Search query cannot be null or empty.");
            if (page <= 0 || pageSize <= 0)
                throw new ArgumentException("Page and page size must be greater than zero.");

            var filter = Builders<Monster>.Filter.Regex(
                m => m.Name,
                new BsonRegularExpression(query, "i"));

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
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            _logger.Information("Starting monster search with criteria {@Criteria}", criteria);

            var filter = BuildSearchFilter(criteria);
            var sort = BuildSearchSort(criteria);

            var page = criteria.Page <= 0 ? 1 : criteria.Page;
            var pageSize = criteria.PageSize <= 0 ? 10 : criteria.PageSize;

            var monsters = await _collection
                .Find(filter)
                .Sort(sort)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            _logger.Information("Completed monster search, returning {Count} results", monsters.Count);
            return monsters;
        }

        private FilterDefinition<Monster> BuildSearchFilter(MonsterSearchCriteria criteria)
        {
            var f = Builders<Monster>.Filter;

            var filter = f.Ne(m => m.IsDeleted, true);

            if (!string.IsNullOrWhiteSpace(criteria.Name))
            {
                filter &= f.Regex(
                    m => m.Name,
                    new BsonRegularExpression(criteria.Name, "i"));
            }

            if (!string.IsNullOrWhiteSpace(criteria.Type))
            {
                var typeRegex = new BsonRegularExpression(criteria.Type, "i");
                filter &= f.Or(
                    f.Regex("Type", typeRegex),
                    f.Regex("Type.Type", typeRegex)
                );
            }

            if (criteria.Tags?.Any() == true)
            {
                filter &= f.AnyIn(m => m.Type!.Tags, criteria.Tags);
            }

            if (criteria.MinCR.HasValue)
            {
                filter &= f.Gte(m => m.CR, criteria.MinCR.Value);
            }

            if (criteria.MaxCR.HasValue)
            {
                filter &= f.Lte(m => m.CR, criteria.MaxCR.Value);
            }

            return filter;
        }

        private SortDefinition<Monster> BuildSearchSort(MonsterSearchCriteria criteria)
        {
            var sortField = string.IsNullOrWhiteSpace(criteria.SortBy)
                ? nameof(Monster.Name)
                : criteria.SortBy;

            return criteria.SortDescending
                ? Builders<Monster>.Sort.Descending(sortField)
                : Builders<Monster>.Sort.Ascending(sortField);
        }
    }
}
