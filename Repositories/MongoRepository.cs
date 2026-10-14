using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class MongoRepository<T> : IRepository<T> where T : class, IEntity
    {
        protected readonly IMongoCollection<T> _collection;
        protected readonly ILogger _logger;
        protected readonly IMemoryCache? _cache;
        protected readonly MemoryCacheEntryOptions _cacheOptions;
        private static bool _campaignIndexCreated;

        public MongoRepository(ILogger logger, IMemoryCache cache, MongoDbContext context, string collectionName)
        {
            _collection = context.GetCollection<T>(collectionName)
                ?? throw new ArgumentNullException(nameof(context), $"Failed to load {collectionName} collection from context: {context}");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger), "Logger cannot be null");
            _cache = cache ?? null;

            _cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(5))
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(30));

            if (!_campaignIndexCreated && typeof(ICampaignScoped).IsAssignableFrom(typeof(T)))
            {
                _collection.Indexes.CreateOne(new CreateIndexModel<T>(
                    Builders<T>.IndexKeys.Ascending(nameof(ICampaignScoped.CampaignId)),
                    new CreateIndexOptions { Name = "idx_campaignId" }));
                _campaignIndexCreated = true;
            }
        }

        /// <summary>Creates indexes, logging instead of throwing so a conflicting existing index can't break every request.</summary>
        protected void TryCreateIndexes(params CreateIndexModel<T>[] models)
        {
            try { _collection.Indexes.CreateMany(models); }
            catch (MongoException ex) { _logger.Warning(ex, "Could not create indexes on {Collection}", typeof(T).Name); }
        }

        // Debug level: these run on nearly every read, so they stay out of production logs.
        protected string GetCacheKey(string id) => $"{typeof(T).Name}_{id}";

        public void AddToCache(T entity)
        {
            if (_cache == null || entity.Id == null) return;
            _cache.Set(GetCacheKey(entity.Id), entity, _cacheOptions);
            _logger.Debug("[CACHE ADD] {Collection} {EntityId}", typeof(T).Name, entity.Id);
        }

        public T? GetFromCache(string id)
        {
            if (_cache == null) return null;
            var hit = _cache.TryGetValue(GetCacheKey(id), out T? cachedEntity);
            _logger.Debug(hit ? "[CACHE HIT] {Collection} {EntityId}" : "[CACHE MISS] {Collection} {EntityId}", typeof(T).Name, id);
            return hit ? cachedEntity : null;
        }

        public void UpdateCache(T entity) => AddToCache(entity);

        public void RemoveFromCache(string id)
        {
            if (_cache == null) return;
            _cache.Remove(GetCacheKey(id));
            _logger.Debug("[CACHE REMOVE] {Collection} {EntityId}", typeof(T).Name, id);
        }

        public async Task<T?> CreateAsync(T entity)
        {
            try
            {
                entity.CreatedAt ??= DateTime.UtcNow;
                entity.UpdatedAt = DateTime.UtcNow;
                entity.IsDeleted = false;

                await _collection.InsertOneAsync(entity);
                AddToCache(entity);

                _logger.Information("Added entity {EntityId} to collection {Collection}", entity.Id, typeof(T).Name);
                return entity;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to add entity to collection {Collection}", typeof(T).Name);
                return null;
            }
        }

        public async Task<List<T>> CreateManyAsync(List<T> entities)
        {
            if (entities.Count == 0) return new List<T>();

            foreach (var entity in entities)
            {
                entity.CreatedAt ??= DateTime.UtcNow;
                entity.UpdatedAt = DateTime.UtcNow;
                entity.IsDeleted = false;
            }

            // One round trip; unordered so a bad document doesn't stop the rest.
            var failed = new HashSet<int>();
            try
            {
                await _collection.InsertManyAsync(entities, new InsertManyOptions { IsOrdered = false });
            }
            catch (MongoBulkWriteException<T> ex)
            {
                foreach (var error in ex.WriteErrors)
                {
                    failed.Add(error.Index);
                    _logger.Error("Failed to add entity {EntityId} to collection {Collection}: {Error}",
                        entities[error.Index].Id, typeof(T).Name, error.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to add {Count} entities to collection {Collection}", entities.Count, typeof(T).Name);
                return new List<T>();
            }

            var addedEntities = entities.Where((_, i) => !failed.Contains(i)).ToList();
            addedEntities.ForEach(AddToCache);
            return addedEntities;
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                var result = await _collection.DeleteOneAsync(e => e.Id == id);
                if (result.DeletedCount > 0)
                {
                    RemoveFromCache(id);
                    _logger.Information("Deleted entity {EntityId} from collection {Collection}", id, typeof(T).Name);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to delete entity {EntityId} from collection {Collection}", id, typeof(T).Name);
                return false;
            }
        }

        public async Task<bool> LogicDeleteAsync(string id)
        {
            try
            {
                var update = Builders<T>.Update
                    .Set(e => e.IsDeleted, true)
                    .Set(e => e.UpdatedAt, DateTime.UtcNow);

                var result = await _collection.UpdateOneAsync(
                    e => e.Id == id && !e.IsDeleted,
                    update
                );

                if (result.ModifiedCount > 0)
                {
                    RemoveFromCache(id);
                    _logger.Information("Logically deleted entity {EntityId} in collection {Collection}", id, typeof(T).Name);
                    return true;
                }

                _logger.Warning("No entity found to logically delete with ID {EntityId} in collection {Collection}", id, typeof(T).Name);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to logically delete entity {EntityId} in collection {Collection}", id, typeof(T).Name);
                return false;
            }
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            try
            {
                var results = await _collection.FindAsync(e => !e.IsDeleted);
                return await results.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to retrieve all entities from collection {Collection}", typeof(T).Name);
                return new List<T>();
            }
        }

        public async Task<List<T>> FindAsync(FilterDefinition<T> filter)
        {
            var notDeleted = Builders<T>.Filter.Ne(e => e.IsDeleted, true);
            return await _collection.Find(notDeleted & filter).ToListAsync();
        }

        /// <summary>Moves an entity between campaigns (null = core). Normal updates never touch CampaignId.</summary>
        public async Task<bool> SetCampaignAsync(string id, string? campaignId)
        {
            BsonValue value = campaignId == null ? BsonNull.Value : ObjectId.Parse(campaignId);
            // Raw BsonDocument so the value is written as-is (ObjectId or null), independent of the member serializer.
            var update = new BsonDocument("$set", new BsonDocument
            {
                { nameof(ICampaignScoped.CampaignId), value },
                { nameof(IEntity.UpdatedAt), DateTime.UtcNow }
            });
            var result = await _collection.UpdateOneAsync(Builders<T>.Filter.Eq(e => e.Id, id), update);
            RemoveFromCache(id);
            return result.MatchedCount > 0;
        }

        public async Task<T?> GetByIdAsync(string id)
        {
            try
            {
                var cached = GetFromCache(id);
                if (cached != null)
                    return cached;

                var result = await _collection.FindAsync(e => e.Id == id && !e.IsDeleted);
                var entity = await result.FirstOrDefaultAsync();

                if (entity != null)
                    AddToCache(entity);

                return entity;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to retrieve entity {EntityId} from collection {Collection}", id, typeof(T).Name);
                return null;
            }
        }

        public async Task<List<T>> GetByIdsAsync(IEnumerable<string> ids)
        {
            try
            {
                var filter = Builders<T>.Filter.In(e => e.Id, ids) & Builders<T>.Filter.Ne(e => e.IsDeleted, true);
                var results = await _collection.FindAsync(filter);
                return await results.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to retrieve entities by IDs from collection {Collection}", typeof(T).Name);
                return new List<T>();
            }
        }

        public async Task<T?> UpdateAsync(T entity)
        {
            if (entity.Id == null)
            {
                _logger.Warning("Attempted to update an entity without an ID in collection {Collection}", typeof(T).Name);
                return null;
            }

            try
            {
                var existing = await _collection.Find(e => e.Id == entity.Id && !e.IsDeleted).FirstOrDefaultAsync();
                if (existing == null)
                {
                    _logger.Warning("No entity found with ID {EntityId} in collection {Collection}", entity.Id, typeof(T).Name);
                    return null;
                }

                var properties = typeof(T).GetProperties();
                var updates = new List<UpdateDefinition<T>>();

                foreach (var prop in properties)
                {
                    if (prop.Name is nameof(IEntity.Id) or "CreatedAt" or "IsDeleted" or nameof(ICampaignScoped.CampaignId))
                        continue;

                    var newValue = prop.GetValue(entity);
                    var oldValue = prop.GetValue(existing);

                    if (newValue != null && !Equals(newValue, oldValue))
                    {
                        updates.Add(Builders<T>.Update.Set(prop.Name, newValue));
                    }
                }

                updates.Add(Builders<T>.Update.Set(nameof(IEntity.UpdatedAt), DateTime.UtcNow));

                if (!updates.Any())
                {
                    _logger.Debug("No changes detected for entity {EntityId} in collection {Collection}", entity.Id, typeof(T).Name);
                    return existing;
                }

                var combinedUpdate = Builders<T>.Update.Combine(updates);
                var updated = await _collection.FindOneAndUpdateAsync<T>(
                    e => e.Id == entity.Id && !e.IsDeleted,
                    combinedUpdate,
                    new FindOneAndUpdateOptions<T> { ReturnDocument = ReturnDocument.After });

                if (updated == null)
                {
                    _logger.Warning("No entity updated for {EntityId} in collection {Collection}", entity.Id, typeof(T).Name);
                    return null;
                }

                UpdateCache(updated);
                _logger.Information("Partially updated entity {EntityId} in collection {Collection}", entity.Id, typeof(T).Name);
                return updated;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to partially update entity {EntityId} in collection {Collection}", entity.Id, typeof(T).Name);
                return null;
            }
        }


        public async Task<long> CountAsync()
        {
            try
            {
                return await _collection.CountDocumentsAsync(e => !e.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to count documents in collection {Collection}", typeof(T).Name);
                return 0;
            }
        }

        public async Task<bool> ExistsAsync(string id)
        {
            try
            {
                var count = await _collection.CountDocumentsAsync(e => e.Id == id && !e.IsDeleted);
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to check existence of entity {EntityId} in collection {Collection}", id, typeof(T).Name);
                return false;
            }
        }

        public async Task<PagedResult<T>> GetAllPaginatedAsync(int page, int pageSize)
        {
            try
            {
                var count = await _collection.CountDocumentsAsync(e => !e.IsDeleted);
                var results = await _collection.Find(e => !e.IsDeleted)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync();

                return new PagedResult<T>
                {
                    Items = results,
                    TotalItems = (int)count,
                    Page = page,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to retrieve paginated entities from collection {Collection}", typeof(T).Name);
                return new PagedResult<T>
                {
                    Items = new List<T>(),
                    TotalItems = 0,
                    Page = page,
                    PageSize = pageSize
                };
            }
        }
    }
}
