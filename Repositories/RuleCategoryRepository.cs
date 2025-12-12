using dndhelper.Database;
using dndhelper.Models.RuleModels;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class RuleCategoryRepository : MongoRepository<RuleCategory>, IRuleCategoryRepository
    {
        private static bool _indexesCreated;

        public RuleCategoryRepository(MongoDbContext context, IMemoryCache cache, ILogger logger)
            : base(logger, cache, context, "RuleCategories")
        {
            EnsureIndexes();
        }

        public async Task<RuleCategory?> GetBySlugAsync(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentNullException(nameof(slug));

            var filter = Builders<RuleCategory>.Filter.And(
                Builders<RuleCategory>.Filter.Ne(c => c.IsDeleted, true),
                Builders<RuleCategory>.Filter.Eq(c => c.Slug, slug));

            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> SlugExistsAsync(string slug, string? excludeId = null)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return false;

            var filter = Builders<RuleCategory>.Filter.Eq(c => c.Slug, slug);

            if (!string.IsNullOrWhiteSpace(excludeId))
            {
                filter &= Builders<RuleCategory>.Filter.Ne(c => c.Id, excludeId);
            }

            filter &= Builders<RuleCategory>.Filter.Ne(c => c.IsDeleted, true);

            var count = await _collection.CountDocumentsAsync(filter);
            return count > 0;
        }

        private void EnsureIndexes()
        {
            if (_indexesCreated)
                return;

            var indexModels = new List<CreateIndexModel<RuleCategory>>
            {
                new CreateIndexModel<RuleCategory>(
                    Builders<RuleCategory>.IndexKeys.Ascending(c => c.Slug),
                    new CreateIndexOptions { Name = "idx_rulecategories_slug_unique", Unique = true }),

                new CreateIndexModel<RuleCategory>(
                    Builders<RuleCategory>.IndexKeys.Ascending(c => c.Order),
                    new CreateIndexOptions { Name = "idx_rulecategories_order" })
            };

            _collection.Indexes.CreateMany(indexModels);
            _indexesCreated = true;
            _logger.Information("RuleCategory indexes ensured (slug unique, order).");
        }
    }
}
