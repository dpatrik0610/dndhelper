using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    /// <summary>
    /// Uncached: messages are read as pages of a campaign's history.
    /// Pages go by _id, which is time-ordered and unique, so equal timestamps can't skip or repeat a message.
    /// Registered as a singleton so the index is ensured once.
    /// </summary>
    public class ChatRepository : IChatRepository
    {
        private readonly IMongoCollection<ChatMessage> _collection;

        public ChatRepository(MongoDbContext context)
        {
            _collection = context.GetCollection<ChatMessage>("ChatMessages");
            _collection.Indexes.CreateOne(new CreateIndexModel<ChatMessage>(
                Builders<ChatMessage>.IndexKeys.Ascending(m => m.CampaignId).Descending(m => m.Id)));
        }

        public Task AddAsync(ChatMessage message) => _collection.InsertOneAsync(message);

        public async Task<ChatMessage?> GetAsync(string id) =>
            await _collection.Find(m => m.Id == id && !m.IsDeleted).FirstOrDefaultAsync();

        public Task ReplaceAsync(ChatMessage message) => _collection.ReplaceOneAsync(m => m.Id == message.Id, message);

        public Task<List<ChatMessage>> PageAsync(string campaignId, ChatMember viewer, string? beforeId, int limit)
        {
            var f = Builders<ChatMessage>.Filter;
            var filter = f.Eq(m => m.CampaignId, campaignId) & f.Eq(m => m.IsDeleted, false);
            // Same rule as ChatMessage.VisibleTo. Ne(true) also matches messages saved before whispers existed.
            if (!viewer.IsDm)
                filter &= f.Ne(m => m.Whisper, true) | f.Eq(m => m.UserId, viewer.UserId) | f.Eq(m => m.ToUserId, viewer.UserId);
            if (beforeId != null)
                filter &= f.Lt("_id", ObjectId.Parse(beforeId));

            return _collection.Find(filter).SortByDescending(m => m.Id).Limit(limit).ToListAsync();
        }
    }
}
