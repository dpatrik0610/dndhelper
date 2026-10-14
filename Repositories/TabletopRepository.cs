using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    /// <summary>
    /// Uncached on purpose: table state changes many times a minute and every write
    /// goes through TabletopService's per-table lock (load → mutate → replace).
    /// Registered as a singleton so the indexes are ensured once.
    /// </summary>
    public class TabletopRepository : ITabletopRepository
    {
        private readonly IMongoCollection<Tabletop> _collection;
        private readonly ILogger _logger;

        public TabletopRepository(ILogger logger, MongoDbContext context)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _collection = context.GetCollection<Tabletop>("Tabletops");

            _collection.Indexes.CreateMany(new[]
            {
                new CreateIndexModel<Tabletop>(
                    Builders<Tabletop>.IndexKeys.Ascending(t => t.CampaignId),
                    new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<Tabletop>(
                    Builders<Tabletop>.IndexKeys.Ascending(t => t.JoinCode),
                    new CreateIndexOptions { Unique = true }),
            });
        }

        public async Task<Tabletop?> GetByIdAsync(string id) =>
            await _collection.Find(t => t.Id == id && !t.IsDeleted).FirstOrDefaultAsync();

        public async Task<Tabletop?> GetByJoinCodeAsync(string joinCode) =>
            await _collection.Find(t => t.JoinCode == joinCode && !t.IsDeleted).FirstOrDefaultAsync();

        public Task<bool> AnyUsesMapAsync(string imageUrl) =>
            _collection.Find(t => t.Map.ImageUrl == imageUrl && !t.IsDeleted).AnyAsync();

        public Task<bool> AnyUsesTokenImageAsync(string imageUrl) =>
            _collection.Find(t => t.Tokens.Any(k => k.ImageUrl == imageUrl) && !t.IsDeleted).AnyAsync();

        public async Task<Tabletop?> GetByCampaignIdAsync(string campaignId) =>
            await _collection.Find(t => t.CampaignId == campaignId && !t.IsDeleted).FirstOrDefaultAsync();

        public async Task<Tabletop> GetOrCreateForCampaignAsync(string campaignId)
        {
            var existing = await GetByCampaignIdAsync(campaignId);
            if (existing != null) return existing;

            var table = new Tabletop { CampaignId = campaignId };
            try
            {
                await _collection.InsertOneAsync(table);
                _logger.Information("🎲 Created tabletop {TableId} for campaign {CampaignId}", table.Id, campaignId);
                return table;
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Another DM opened it at the same moment.
                return await GetByCampaignIdAsync(campaignId)
                    ?? throw new InvalidOperationException("Could not create the table, try again.");
            }
        }

        public Task ReplaceAsync(Tabletop table) =>
            _collection.ReplaceOneAsync(t => t.Id == table.Id, table);
    }
}
