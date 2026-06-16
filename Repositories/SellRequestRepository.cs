using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class SellRequestRepository : MongoRepository<SellRequest>, ISellRequestRepository
    {
        public SellRequestRepository(MongoDbContext context, ILogger logger, IMemoryCache cache) 
            : base(logger, cache, context, "SellRequests")
        {
        }

        public async Task<IEnumerable<SellRequest>> GetByCampaignIdAsync(string campaignId)
        {
            var filter = Builders<SellRequest>.Filter.And(
                Builders<SellRequest>.Filter.Eq(r => r.CampaignId, campaignId),
                Builders<SellRequest>.Filter.Eq(r => r.IsDeleted, false)
            );
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<SellRequest?> TryUpdateStatusAsync(string id, SellRequestStatus currentStatus, SellRequestStatus newStatus)
        {
            var filter = Builders<SellRequest>.Filter.And(
                Builders<SellRequest>.Filter.Eq(r => r.Id, id),
                Builders<SellRequest>.Filter.Eq(r => r.Status, currentStatus),
                Builders<SellRequest>.Filter.Eq(r => r.IsDeleted, false)
            );

            var update = Builders<SellRequest>.Update
                .Set(r => r.Status, newStatus)
                .Set(r => r.UpdatedAt, DateTime.UtcNow);

            var options = new FindOneAndUpdateOptions<SellRequest>
            {
                ReturnDocument = ReturnDocument.After
            };

            return await _collection.FindOneAndUpdateAsync(filter, update, options);
        }
    }
}
