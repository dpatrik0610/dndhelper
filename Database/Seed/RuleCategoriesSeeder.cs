using dndhelper.Models.RuleModels;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Database.Seed
{
    public static class RuleCategoriesSeeder
    {
        public static async Task SeedDefaultCategoriesAsync(MongoDbContext context, ILogger logger, CancellationToken cancellationToken = default)
        {
            var collection = context.GetCollection<RuleCategory>("RuleCategories");

            var existingCount = await collection.CountDocumentsAsync(FilterDefinition<RuleCategory>.Empty, cancellationToken: cancellationToken);
            if (existingCount > 0)
            {
                logger.Information("RuleCategories collection already populated; skipping default seed.");
                return;
            }

            var now = DateTime.UtcNow;
            var defaults = new List<RuleCategory>
            {
                new RuleCategory { Slug = "core", Name = "Core", Order = 1, CreatedAt = now, UpdatedAt = now },
                new RuleCategory { Slug = "combat", Name = "Combat", Order = 2, CreatedAt = now, UpdatedAt = now },
                new RuleCategory { Slug = "magic", Name = "Magic", Order = 3, CreatedAt = now, UpdatedAt = now },
                new RuleCategory { Slug = "status", Name = "Status", Order = 4, CreatedAt = now, UpdatedAt = now },
                new RuleCategory { Slug = "equipment", Name = "Equipment", Order = 5, CreatedAt = now, UpdatedAt = now },
                new RuleCategory { Slug = "exploration", Name = "Exploration", Order = 6, CreatedAt = now, UpdatedAt = now },
                new RuleCategory { Slug = "downtime", Name = "Downtime", Order = 7, CreatedAt = now, UpdatedAt = now },
                new RuleCategory { Slug = "homebrew", Name = "Homebrew", Order = 8, CreatedAt = now, UpdatedAt = now }
            };

            await collection.InsertManyAsync(defaults, cancellationToken: cancellationToken);
            logger.Information("Seeded {Count} default rule categories.", defaults.Count);
        }
    }
}
