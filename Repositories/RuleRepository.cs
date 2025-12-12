using dndhelper.Database;
using dndhelper.Models.RuleModels;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class RuleRepository : MongoRepository<Rule>, IRuleRepository
    {
        private static bool _indexesCreated;

        public RuleRepository(MongoDbContext context, IMemoryCache cache, ILogger logger)
            : base(logger, cache, context, "Rules")
        {
            EnsureIndexes();
        }

        public async Task<Rule?> GetBySlugAsync(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentNullException(nameof(slug));

            var filter = Builders<Rule>.Filter.And(
                Builders<Rule>.Filter.Ne(r => r.IsDeleted, true),
                Builders<Rule>.Filter.Eq(r => r.Slug, slug)
            );

            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> SlugExistsAsync(string slug, string? excludeId = null)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return false;

            var filter = Builders<Rule>.Filter.Eq(r => r.Slug, slug);

            if (!string.IsNullOrWhiteSpace(excludeId))
            {
                filter &= Builders<Rule>.Filter.Ne(r => r.Id, excludeId);
            }

            filter &= Builders<Rule>.Filter.Ne(r => r.IsDeleted, true);

            var count = await _collection.CountDocumentsAsync(filter);
            return count > 0;
        }

        public async Task<RuleQueryResult> QueryAsync(RuleQueryOptions options)
        {
            var normalizedLimit = NormalizeLimit(options.Limit);
            var baseFilter = BuildBaseFilter(options);
            var cursorFilter = BuildCursorFilter(options.Cursor);
            var sort = Builders<Rule>.Sort
                .Descending(r => r.UpdatedAt)
                .Descending(r => r.Id);

            try
            {
                var total = await _collection.CountDocumentsAsync(baseFilter);

                var items = await _collection.Find(baseFilter & cursorFilter)
                    .Sort(sort)
                    .Limit(normalizedLimit)
                    .ToListAsync();

                return new RuleQueryResult
                {
                    Items = items,
                    Total = total,
                    NextCursor = BuildNextCursor(items, normalizedLimit)
                };
            }
            catch (MongoCommandException ex) when (ex.CodeName == "IndexNotFound" || ex.Message.Contains("$text"))
            {
                // Fallback to regex search when text index is unavailable
                baseFilter = BuildBaseFilter(options, preferRegexSearch: true);
                var total = await _collection.CountDocumentsAsync(baseFilter);

                var items = await _collection.Find(baseFilter & cursorFilter)
                    .Sort(sort)
                    .Limit(normalizedLimit)
                    .ToListAsync();

                return new RuleQueryResult
                {
                    Items = items,
                    Total = total,
                    NextCursor = BuildNextCursor(items, normalizedLimit)
                };
            }
        }

        public async Task<RuleStats> GetStatsAsync()
        {
            var match = Builders<Rule>.Filter.Ne(r => r.IsDeleted, true);

            var byCategory = await _collection.Aggregate()
                .Match(match)
                .Group(
                    rule => rule.Category,
                    g => new { Category = g.Key, Count = g.Count() })
                .ToListAsync();

            var tagsPipeline = new[]
            {
                new BsonDocument("$match", new BsonDocument("IsDeleted", new BsonDocument("$ne", true))),
                new BsonDocument("$unwind", "$tags"),
                new BsonDocument("$group", new BsonDocument { { "_id", "$tags" }, { "count", new BsonDocument("$sum", 1) } }),
                new BsonDocument("$sort", new BsonDocument("count", -1)),
                new BsonDocument("$limit", 20)
            };

            var topTagsRaw = await _collection.Aggregate<BsonDocument>(PipelineDefinition<Rule, BsonDocument>.Create(tagsPipeline)).ToListAsync();
            var topTags = topTagsRaw.Select(t => new RuleTagCount
            {
                Tag = t.GetValue("_id", string.Empty).AsString,
                Count = t.GetValue("count", 0).ToInt64()
            }).ToList();

            var total = await _collection.CountDocumentsAsync(match);

            return new RuleStats
            {
                ByCategory = byCategory.ToDictionary(x => x.Category ?? "Uncategorized", x => (long)x.Count),
                TopTags = topTags,
                Total = total
            };
        }

        private void EnsureIndexes()
        {
            if (_indexesCreated)
                return;

            var indexModels = new List<CreateIndexModel<Rule>>
            {
                new CreateIndexModel<Rule>(
                    Builders<Rule>.IndexKeys.Ascending(r => r.Slug),
                    new CreateIndexOptions { Name = "idx_rules_slug_unique", Unique = true }),

                new CreateIndexModel<Rule>(
                    Builders<Rule>.IndexKeys.Ascending(r => r.Category),
                    new CreateIndexOptions { Name = "idx_rules_category" }),

                new CreateIndexModel<Rule>(
                    Builders<Rule>.IndexKeys.Ascending(r => r.Tags),
                    new CreateIndexOptions { Name = "idx_rules_tags" }),

                new CreateIndexModel<Rule>(
                    Builders<Rule>.IndexKeys
                        .Text(r => r.Title)
                        .Text(r => r.Summary)
                        .Text(r => r.Tags)
                        .Text(r => r.Body),
                    new CreateIndexOptions { Name = "idx_rules_text", DefaultLanguage = "english" })
            };

            _collection.Indexes.CreateMany(indexModels);
            _indexesCreated = true;
            _logger.Information("Rule indexes ensured (slug unique, category, tags, text).");
        }

        private FilterDefinition<Rule> BuildBaseFilter(RuleQueryOptions options, bool preferRegexSearch = false)
        {
            var filter = Builders<Rule>.Filter.Ne(r => r.IsDeleted, true);

            if (!string.IsNullOrWhiteSpace(options.Category))
            {
                filter &= Builders<Rule>.Filter.Eq(r => r.Category, options.Category);
            }

            if (!string.IsNullOrWhiteSpace(options.Tag))
            {
                filter &= Builders<Rule>.Filter.AnyEq(r => r.Tags, options.Tag);
            }

            if (!string.IsNullOrWhiteSpace(options.Source))
            {
                filter &= Builders<Rule>.Filter.Regex(
                    "source.title",
                    new BsonRegularExpression(options.Source, "i"));
            }

            if (!string.IsNullOrWhiteSpace(options.Search))
            {
                if (preferRegexSearch)
                {
                    filter &= BuildRegexSearchFilter(options.Search);
                }
                else
                {
                    filter &= BuildTextSearchFilter(options.Search);
                }
            }

            return filter;
        }

        private static FilterDefinition<Rule> BuildTextSearchFilter(string search)
        {
            return Builders<Rule>.Filter.Text(search, new TextSearchOptions { Language = "english" });
        }

        private static FilterDefinition<Rule> BuildRegexSearchFilter(string search)
        {
            var regex = new BsonRegularExpression(search, "i");

            return Builders<Rule>.Filter.Or(
                Builders<Rule>.Filter.Regex(r => r.Title, regex),
                Builders<Rule>.Filter.Regex(r => r.Summary, regex),
                Builders<Rule>.Filter.Regex("tags", regex),
                Builders<Rule>.Filter.Regex("body", regex));
        }

        private static FilterDefinition<Rule> BuildCursorFilter(string? cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor))
                return Builders<Rule>.Filter.Empty;

            if (!TryDecodeCursor(cursor, out var updatedAt, out var id) || string.IsNullOrWhiteSpace(id))
                return Builders<Rule>.Filter.Empty;

            if (!ObjectId.TryParse(id, out var objectId))
                return Builders<Rule>.Filter.Empty;

            var filters = new List<FilterDefinition<Rule>>();
            var builder = Builders<Rule>.Filter;

            if (updatedAt.HasValue)
            {
                filters.Add(builder.Lt(r => r.UpdatedAt, updatedAt.Value));
                filters.Add(builder.And(
                    builder.Eq(r => r.UpdatedAt, updatedAt.Value),
                    builder.Lt("_id", objectId)));

                return builder.Or(filters);
            }

            return builder.Lt("_id", objectId);
        }

        private static string? BuildNextCursor(IReadOnlyList<Rule> items, int limit)
        {
            if (items == null || items.Count == 0 || items.Count < limit)
                return null;

            var last = items[^1];
            if (string.IsNullOrWhiteSpace(last.Id))
                return null;

            var updated = last.UpdatedAt?.ToUniversalTime().ToString("o") ?? string.Empty;
            var payload = $"{updated}|{last.Id}";

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        }

        private static bool TryDecodeCursor(string cursor, out DateTime? updatedAt, out string? id)
        {
            updatedAt = null;
            id = null;

            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                var parts = decoded.Split('|');

                if (parts.Length >= 1 && DateTime.TryParse(parts[0], out var parsedDate))
                {
                    updatedAt = parsedDate;
                }

                if (parts.Length >= 2)
                {
                    id = parts[1];
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static int NormalizeLimit(int limit)
        {
            if (limit <= 0) return 20;
            if (limit > 100) return 100;
            return limit;
        }
    }
}
