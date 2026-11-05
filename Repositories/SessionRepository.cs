using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Utils;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class SessionRepository : MongoRepository<Session>, ISessionRepository
    {
        public SessionRepository(ILogger logger, IMemoryCache cache, MongoDbContext context)
            : base(logger, cache, context, "Sessions")
        {
        }

        public async Task<List<Session>> GetByCampaignIdAsync(string campaignId)
        {
            var filter = Builders<Session>.Filter.Eq(s => s.CampaignId, campaignId);
            return await FindAsync(filter);
        }

        public async Task<Session> ReplaceAsync(Session session)
        {
            session.UpdatedAt = DateTime.UtcNow;
            var result = await _collection.ReplaceOneAsync(s => s.Id == session.Id && !s.IsDeleted, session);
            if (result.MatchedCount == 0)
                throw new NotFoundException("Session not found.");

            UpdateCache(session);
            return session;
        }
    }
}
