using dndhelper.Models.RuleModels;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Database.Seed
{
    public static class RulesSeeder
    {
        public static async Task SeedSampleRulesAsync(MongoDbContext context, ILogger logger, CancellationToken cancellationToken = default)
        {
            var collection = context.GetCollection<Rule>("Rules");

            var existingCount = await collection.CountDocumentsAsync(FilterDefinition<Rule>.Empty, cancellationToken: cancellationToken);
            if (existingCount > 0)
            {
                logger.Information("Rules collection already populated; skipping sample seed.");
                return;
            }

            var sampleRules = new List<Rule>
            {
                new Rule
                {
                    Slug = "sample-action-economy",
                    Title = "Action Economy",
                    Category = RuleCategory.Core.ToString(),
                    Summary = "Placeholder rule explaining how actions, bonus actions, and movement work.",
                    Tags = new List<string> { "actions", "turns" },
                    Body = new List<string>
                    {
                        "On your turn, you can typically take one action and move up to your speed.",
                        "Some class features and spells grant bonus actions or reactions that can be used when triggered."
                    },
                    Sources = new List<RuleSource>
                    {
                        new RuleSource { Title = "Player's Handbook", Page = "189", Edition = "5e" }
                    },
                    Examples = new List<RuleExample>
                    {
                        new RuleExample
                        {
                            Title = "Dash and Disengage",
                            Description = "A rogue uses Cunning Action to dash as a bonus action after disengaging.",
                            Outcome = "Avoids opportunity attacks and doubles movement for the turn."
                        }
                    },
                    RelatedRuleSlugs = new List<string> { "movement", "bonus-actions" },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsDeleted = false
                }
            };

            await collection.InsertManyAsync(sampleRules, cancellationToken: cancellationToken);
            logger.Information("Inserted {Count} sample rules. Replace them with your own data when ready.", sampleRules.Count);
        }
    }
}
