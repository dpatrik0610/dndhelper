using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
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

        public MongoRepository(ILogger logger, IMemoryCache cache, MongoDbContext context, string collectionName)
        {
            _collection = context.GetCollection<T>(collectionName)
                ?? throw new ArgumentNullException(nameof(context), $"Failed to load {collectionName} collection from context: {context}");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger), "Logger cannot be null");
            _cache = cache ?? null; // Cache can be null for repositories that don't use caching.

            _cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(5))
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(30));
        }

        // ------------------------
        // CACHE UTILITIES (VISUAL)
        // ------------------------
        protected string GetCacheKey(string id) => $"{typeof(T).Name}_{id}";

        public void AddToCache(T entity)
        {
            if (_cache == null || entity.Id == null) return;
            var key = GetCacheKey(entity.Id);
            _cache.Set(key, entity, _cacheOptions);

            _logger.Information(
                $"🟢 [CACHE ADD] {typeof(T).Name} | ID: {entity.Id} | CacheKey: {key} | Items: {GetItemCount(entity)}"
            );
        }

        public T? GetFromCache(string id)
        {
            if (_cache == null) return null;
            var key = GetCacheKey(id);
            if (_cache.TryGetValue(key, out T cachedEntity))
            {
                _logger.Information(
                    $"🟡 [CACHE HIT] {typeof(T).Name} | ID: {id} | CacheKey: {key} | Items: {GetItemCount(cachedEntity)}"
                );
                return cachedEntity;
            }

            _logger.Information(
                $"⚪ [CACHE MISS] {typeof(T).Name} | ID: {id} | CacheKey: {key}"
            );
            return null;
        }

        public void UpdateCache(T entity)
        {
            if (_cache == null || entity.Id == null) return;
            var key = GetCacheKey(entity.Id);
            _cache.Set(key, entity, _cacheOptions);

            _logger.Information(
                $"🔵 [CACHE UPDATE] {typeof(T).Name} | ID: {entity.Id} | CacheKey: {key} | Items: {GetItemCount(entity)}"
            );
        }

        public void RemoveFromCache(string id)
        {
            if (_cache == null) return;
            var key = GetCacheKey(id);
            _cache.Remove(key);

            _logger.Information(
                $"🔴 [CACHE REMOVE] {typeof(T).Name} | ID: {id} | CacheKey: {key}"
            );
        }


        public List<string> GetAllCachedIds()
        {
            if (_cache == null)
                return new List<string>();

            var cachedIds = new List<string>();
            // IMemoryCache doesn't expose direct enumeration, so we use reflection:
            var entriesField = typeof(MemoryCache).GetProperty("EntriesCollection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (entriesField?.GetValue(_cache) is ICollection<object> entries)
            {
                foreach (var entry in entries)
                {
                    var keyProperty = entry.GetType().GetProperty("Key");
                    var keyValue = keyProperty?.GetValue(entry)?.ToString();

                    if (!string.IsNullOrEmpty(keyValue) && keyValue.StartsWith(typeof(T).Name))
                    {
                        var id = keyValue.Replace($"{typeof(T).Name}_", "");
                        cachedIds.Add(id);
                    }
                }
            }

            _logger.Information("Cached IDs for {Collection}: {Ids}", typeof(T).Name, string.Join(", ", cachedIds));
            return cachedIds;
        }

        // ------------------------
        // HELPER
        // ------------------------
        private int GetItemCount(T entity)
        {
            // If entity has Items (like Inventory), count them
            var itemsProp = typeof(T).GetProperty("Items");
            if (itemsProp != null && itemsProp.GetValue(entity) is IEnumerable<object> items)
                return items.Count();

            return 0;
        }


        // ------------------------
        // REPOSITORY METHODS
        // ------------------------

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
            var addedEntities = new List<T>();
            foreach (var entity in entities)
            {
                try
                {
                    entity.CreatedAt ??= DateTime.UtcNow;
                    entity.UpdatedAt = DateTime.UtcNow;
                    entity.IsDeleted = false;

                    await _collection.InsertOneAsync(entity);
                    AddToCache(entity);

                    addedEntities.Add(entity);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to add entity {EntityId} to collection {Collection}", entity.Id, typeof(T).Name);
                }
            }
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

        public async Task<T?> GetByIdAsync(string id)
        {
            try
            {
                var cached = GetFromCache(id);
                if (cached != null)
                {
                    _logger.Debug("Cache hit for entity {EntityId} in {Collection}", id, typeof(T).Name);
                    return cached;
                }

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
            var results = new List<T>();
            foreach (var id in ids)
            {
                var entity = await GetByIdAsync(id);
                if (entity != null)
                {
                    results.Add(entity);
                }
            }
            return results;
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

                // Get all writable properties
                var properties = typeof(T).GetProperties();
                var updates = new List<UpdateDefinition<T>>();

                foreach (var prop in properties)
                {
                    // Skip nulls and immutable fields
                    if (prop.Name is nameof(IEntity.Id) or "CreatedAt" or "IsDeleted")
                        continue;

                    var newValue = prop.GetValue(entity);
                    var oldValue = prop.GetValue(existing);

                    // Only update changed values (handles both value and reference types)
                    if (newValue != null && !Equals(newValue, oldValue))
                    {
                        updates.Add(Builders<T>.Update.Set(prop.Name, newValue));
                    }
                }

                // Always refresh UpdatedAt
                updates.Add(Builders<T>.Update.Set(nameof(IEntity.UpdatedAt), DateTime.UtcNow));

                if (!updates.Any())
                {
                    _logger.Debug("No changes detected for entity {EntityId} in collection {Collection}", entity.Id, typeof(T).Name);
                    return existing;
                }

                var combinedUpdate = Builders<T>.Update.Combine(updates);
                var result = await _collection.UpdateOneAsync(e => e.Id == entity.Id, combinedUpdate);

                if (result.ModifiedCount > 0)
                {
                    var updated = await _collection.Find(e => e.Id == entity.Id && !e.IsDeleted).FirstOrDefaultAsync();
                    if (updated != null)
                        UpdateCache(updated);

                    _logger.Information("Partially updated entity {EntityId} in collection {Collection}", entity.Id, typeof(T).Name);
                    return updated;
                }

                _logger.Warning("No entity updated for {EntityId} in collection {Collection}", entity.Id, typeof(T).Name);
                return existing;
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
    }
}
