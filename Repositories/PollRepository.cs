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
    public class PollRepository : MongoRepository<Poll>, IPollRepository
    {
        public PollRepository(ILogger logger, IMemoryCache cache, MongoDbContext context)
            : base(logger, cache, context, "Polls")
        {
        }

        public async Task<List<Poll>> GetByCampaignIdAsync(string campaignId)
        {
            var filter = Builders<Poll>.Filter.Eq(p => p.CampaignId, campaignId)
                & Builders<Poll>.Filter.Eq(p => p.IsDeleted, false);

            return await _collection.Find(filter).SortByDescending(p => p.CreatedAt).ToListAsync();
        }

        public async Task<Poll?> GetFreshAsync(string id) =>
            await _collection.Find(p => p.Id == id && !p.IsDeleted).FirstOrDefaultAsync();

        public async Task<bool> TryReplaceAsync(Poll poll, int expectedVersion)
        {
            poll.Version = expectedVersion + 1;
            poll.UpdatedAt = DateTime.UtcNow;

            var result = await _collection.ReplaceOneAsync(
                p => p.Id == poll.Id && p.Version == expectedVersion, poll);

            if (result.MatchedCount == 0)
            {
                poll.Version = expectedVersion;
                return false;
            }

            UpdateCache(poll);
            return true;
        }
    }
}
