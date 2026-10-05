using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class CampaignRepository : MongoRepository<Campaign>, ICampaignRepository
    {
        private static bool _indexesCreated;

        public CampaignRepository(ILogger logger, IMemoryCache cache, MongoDbContext context)
            : base(logger, cache, context, "Campaigns")
        {
            if (_indexesCreated) return;
            _collection.Indexes.CreateMany(new[]
            {
                new CreateIndexModel<Campaign>(
                    Builders<Campaign>.IndexKeys.Ascending("Members.UserId"),
                    new CreateIndexOptions { Name = "idx_campaigns_members" }),
                new CreateIndexModel<Campaign>(
                    Builders<Campaign>.IndexKeys.Ascending(c => c.InviteCode),
                    // Partial, not sparse: un-migrated docs store InviteCode as an explicit null.
                    new CreateIndexOptions<Campaign>
                    {
                        Name = "idx_campaigns_invite_unique",
                        Unique = true,
                        PartialFilterExpression = Builders<Campaign>.Filter.Type(c => c.InviteCode, BsonType.String)
                    }),
            });
            _indexesCreated = true;
        }

        public async Task<List<Campaign>> GetForMemberAsync(string userId)
        {
            if (!ObjectId.TryParse(userId, out var oid)) return new List<Campaign>();
            var filter = Builders<Campaign>.Filter.Eq("Members.UserId", oid)
                & Builders<Campaign>.Filter.Ne(c => c.IsDeleted, true);
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<Campaign?> GetByInviteCodeAsync(string code) =>
            await _collection.Find(c => c.InviteCode == code && !c.IsDeleted).FirstOrDefaultAsync();
    }
}
