using dndhelper.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Database.Migrations
{
    /// <summary>
    /// Rewrites monsters from the old 5etools-shaped documents to the flat Monster model, in place
    /// (same _id, campaign and deleted flag). Idempotent: only documents without a "Name" field are touched,
    /// so a second run converts nothing. Take a backup (Backups tab) before running it for real.
    /// </summary>
    public class MonsterModelMigration
    {
        private readonly MongoDbContext _db;
        private readonly ILogger _logger;

        public MonsterModelMigration(MongoDbContext db, ILogger logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<MonsterMigrationReport> RunAsync(bool dryRun)
        {
            var collection = _db.GetCollection<BsonDocument>("Monsters");
            var legacy = (await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync())
                .Where(FiveEToolsMonster.IsLegacy)
                .ToList();

            var converter = new FiveEToolsMonster(legacy);
            var converted = legacy.Select(converter.Convert).ToList();

            if (!dryRun)
                foreach (var chunk in converted.Chunk(500))
                    await collection.BulkWriteAsync(chunk.Select(m => new ReplaceOneModel<BsonDocument>(
                        new BsonDocument("_id", ObjectId.Parse(m.Id)), m.ToBsonDocument())));

            var report = new MonsterMigrationReport
            {
                DryRun = dryRun,
                Legacy = legacy.Count,
                Converted = dryRun ? 0 : converted.Count,
                CopiesResolved = converter.CopiesResolved,
                MissingParents = converter.MissingParents,
                SkippedMods = converter.Unsupported,
                Sample = converted.FirstOrDefault(m => m.Actions.Count > 0 && m.Traits.Count > 0),
            };
            _logger.Information("Monster model migration {Mode}: {Legacy} legacy, {Converted} converted, {Copies} copies resolved, {Missing} without parent",
                dryRun ? "dry run" : "applied", report.Legacy, report.Converted, report.CopiesResolved, report.MissingParents.Count);
            return report;
        }
    }

    public class MonsterMigrationReport
    {
        public bool DryRun { get; set; }
        /// <summary>Documents still in the old shape before this run.</summary>
        public int Legacy { get; set; }
        public int Converted { get; set; }
        public int CopiesResolved { get; set; }
        /// <summary>Copies whose parent isn't in the collection: converted from their own fields only.</summary>
        public List<string> MissingParents { get; set; } = new();
        /// <summary>5etools _mod edits not applied (spell list tweaks, templates), by kind.</summary>
        public Dictionary<string, int> SkippedMods { get; set; } = new();
        /// <summary>One converted monster, to eyeball the result of a dry run.</summary>
        public Monster? Sample { get; set; }
    }
}
