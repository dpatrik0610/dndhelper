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
    public class MonsterRepository : IMonsterRepository
    {
        private readonly IMongoCollection<Monster> _monsters;
        private readonly IMemoryCache _cache;
        private readonly ILogger _logger;

        public MonsterRepository(MongoDbContext context, IMemoryCache cache, ILogger logger)
        {
            _monsters = context.GetCollection<Monster>("Monsters");
            _cache = cache;
            _logger = logger;
        }

        // Create
        public async Task CreateAsync(Monster monster)
        {
            if (monster == null || string.IsNullOrWhiteSpace(monster.Name))
                throw new ArgumentException("Monster or monster name is null.");

            await _monsters.InsertOneAsync(monster);

            _cache.Set($"monster_{monster.Id}", monster, TimeSpan.FromHours(1));
            _cache.Remove("all_monsters");
        }

        // Read
        public async Task<Monster?> GetByIdAsync(string id)
        {
            if (_cache.TryGetValue($"monster_{id}", out Monster cachedMonster))
            {
                _logger.Information($"Monster {id} retrieved from cache.");
                return cachedMonster;
            }

            var monster = await _monsters.Find(m => m.Id == id).FirstOrDefaultAsync();
            if (monster != null)
                _cache.Set($"monster_{id}", monster, TimeSpan.FromHours(1));
            return monster;
        }

        public async Task<List<Monster>> FindByNamePhraseAsync(string namePhrase)
        {
            if (string.IsNullOrWhiteSpace(namePhrase))
                throw new ArgumentException("Monster name phrase is null or empty.");

            string cacheKey = $"monster_name_phrase_{namePhrase.ToLower()}";
            if (_cache.TryGetValue(cacheKey, out List<Monster> cachedMonsters))
            {
                _logger.Information($"Monsters matching '{namePhrase}' retrieved from cache.");
                return cachedMonsters;
            }

            var filter = Builders<Monster>.Filter.Regex(
                m => m.Name,
                new MongoDB.Bson.BsonRegularExpression(namePhrase, "i"));

            var monsters = await _monsters.Find(filter).ToListAsync();
            if (monsters.Any())
                _cache.Set(cacheKey, monsters, TimeSpan.FromHours(1));

            return monsters;
        }


        public async Task<List<Monster>> GetAllAsync()
        {
            const string cacheKey = "all_monsters";
            if (_cache.TryGetValue(cacheKey, out List<Monster> cachedList))
            {
                _logger.Information("All monsters retrieved from cache.");
                return cachedList!;
            }

            var monsters = await _monsters.Find(_ => true).ToListAsync();
            _cache.Set(cacheKey, monsters, TimeSpan.FromMinutes(30));
            return monsters;
        }

        public async Task<List<Monster>> GetPagedAsync(int page, int pageSize)
        {
            var monsters = await _monsters.Find(_ => true)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return monsters;
        }

        public async Task<List<Monster>> SearchAsync(string query, int page, int pageSize)
        {
            var filter = Builders<Monster>.Filter.Regex(m => m.Name, new MongoDB.Bson.BsonRegularExpression(query, "i"));
            var monsters = await _monsters.Find(filter)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return monsters;
        }

        public async Task<bool> ExistsAsync(string id)
        {
            return await _monsters.CountDocumentsAsync(m => m.Id == id) > 0;
        }

        // Update
        public async Task UpdateAsync(Monster monster)
        {
            if (monster == null || string.IsNullOrWhiteSpace(monster.Id))
                throw new ArgumentException("Monster or monster id is null.");

            
            var result = await _monsters.ReplaceOneAsync(m => m.Id == monster.Id, monster);
            if (result.MatchedCount == 0)
                throw new KeyNotFoundException("Monster not found.");

            
            _cache.Set($"monster_{monster.Id}", monster, TimeSpan.FromHours(1));
            _cache.Remove("all_monsters");
        }

        // Delete
        public async Task DeleteAsync(string id)
        {
            var result = await _monsters.DeleteOneAsync(m => m.Id == id);
            if (result.DeletedCount == 0)
                throw new KeyNotFoundException("Monster not found.");
            _cache.Remove($"monster_{id}");
            _cache.Remove("all_monsters");
        }

        public async Task<bool> LogicDeleteAsync(string id)
        {
            var update = Builders<Monster>.Update.Set("IsDeleted", true);
            var result = await _monsters.UpdateOneAsync(m => m.Id == id, update);
            _cache.Remove($"monster_{id}");
            _cache.Remove("all_monsters");
            return result.ModifiedCount > 0;
        }

        public async Task<List<Monster>> FindByOwnerIdAsync(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
                throw new ArgumentException("Owner ID cannot be null or empty.");

            var filter = Builders<Monster>.Filter.Eq(m => m.CreatedByUserId, ownerId);
            var monsters = await _monsters.Find(filter).ToListAsync();
            return monsters;
        }
        public async Task<List<Monster>> SearchAsync(MonsterSearchCriteria criteria)
        {
            var filterBuilder = Builders<Monster>.Filter;
            var filter = FilterDefinition<Monster>.Empty;

            _logger.Information("Starting monster search with criteria {@Criteria}", criteria);

            if (!string.IsNullOrWhiteSpace(criteria.Name))
            {
                filter &= filterBuilder.Regex(m => m.Name, new BsonRegularExpression(criteria.Name, "i"));
            }

            if (!string.IsNullOrWhiteSpace(criteria.Type))
            {
                var typeFilters = new List<FilterDefinition<Monster>>
                {
                    filterBuilder.Regex("Type", new BsonRegularExpression(criteria.Type, "i")),       // string case
                    filterBuilder.Regex("Type.Type", new BsonRegularExpression(criteria.Type, "i"))    // object case
                };

                filter &= filterBuilder.Or(typeFilters);
            }

            if (criteria.Tags?.Any() == true)
                filter &= filterBuilder.AnyIn(m => m.Type!.Tags, criteria.Tags);

            var sort = criteria.SortDescending
                ? Builders<Monster>.Sort.Descending(criteria.SortBy)
                : Builders<Monster>.Sort.Ascending(criteria.SortBy);

            if (criteria.MinCR != null)
                filter &= filterBuilder.Gte(m => m.CR, criteria.MinCR.Value);
            if (criteria.MaxCR != null)
                filter &= filterBuilder.Lte(m => m.CR, criteria.MaxCR.Value);

            var monsters = await _monsters
                .Find(filter)
                .Sort(sort)
                .Skip((criteria.Page - 1) * criteria.PageSize)
                .Limit(criteria.PageSize)
                .ToListAsync();

            _logger.Information("Fetched {Count} monsters from database", monsters.Count);
            _logger.Information("Completed monster search, returning {Count} results", monsters.Count);

            return monsters;
        }

    }
}