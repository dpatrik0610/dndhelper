using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class MongoRepository<T> : IRepository<T> where T : class, IEntity
    {
        protected readonly IMongoCollection<T> _collection;

        // Accepts a MongoDbContext instead of IMongoDatabase
        public MongoRepository(MongoDbContext context, string collectionName)
        {
            _collection = context.GetCollection<T>(collectionName);
        }

        public async Task<T> AddAsync(T entity)
        {
            if (entity.CreatedAt == null)
                entity.CreatedAt = DateTime.UtcNow;

            entity.UpdatedAt = DateTime.UtcNow;

            await _collection.InsertOneAsync(entity);
            return entity;
        }

        public async Task<bool> DeleteAsync(string id)
        {
            var result = await _collection.DeleteOneAsync(e => e.Id == id);
            return result.DeletedCount > 0;
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            var results = await _collection.FindAsync(_ => true);
            return await results.ToListAsync();
        }

        public async Task<T?> GetByIdAsync(string id)
        {
            var result = await _collection.FindAsync(e => e.Id == id);
            return await result.FirstOrDefaultAsync();
        }

        public async Task<T?> UpdateAsync(T entity)
        {
            entity.UpdatedAt = DateTime.UtcNow;

            var result = await _collection.ReplaceOneAsync(
                e => e.Id == entity.Id,
                entity
            );

            return result.ModifiedCount > 0 ? entity : null;
        }

        public async Task<long> CountAsync()
        {
            return await _collection.CountDocumentsAsync(_ => true);
        }

        public async Task<bool> ExistsAsync(string id)
        {
            var count = await _collection.CountDocumentsAsync(e => e.Id == id);
            return count > 0;
        }
    }
}
