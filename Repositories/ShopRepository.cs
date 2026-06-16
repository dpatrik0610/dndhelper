using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Serilog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class ShopRepository : MongoRepository<Shop>, IShopRepository
    {
        public ShopRepository(MongoDbContext context, ILogger logger, IMemoryCache cache) 
            : base(logger, cache, context, "Shops")
        {
        }

        public async Task<IEnumerable<Shop>> GetByCampaignIdAsync(string campaignId)
        {
            var filter = Builders<Shop>.Filter.And(
                Builders<Shop>.Filter.Eq(s => s.CampaignId, campaignId),
                Builders<Shop>.Filter.Eq(s => s.IsDeleted, false)
            );
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<Shop?> GetByInventoryIdAsync(string inventoryId)
        {
            var filter = Builders<Shop>.Filter.And(
                Builders<Shop>.Filter.Eq(s => s.InventoryId, inventoryId),
                Builders<Shop>.Filter.Eq(s => s.IsDeleted, false)
            );
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }
    }
}
