using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class EquipmentRepository : MongoRepository<Equipment>, IEquipmentRepository
    {
        public EquipmentRepository(MongoDbContext context, ILogger logger, IMemoryCache cache) : base(logger, cache, context, "Equipment") { }

        public async Task<Equipment?> GetByIndexAsync(string index) =>
            await _collection.Find(e => e.Index == index).FirstOrDefaultAsync();

        //public new async Task<Equipment> UpdateAsync(Equipment equipment)
        //{
        //    var filter = Builders<Equipment>.Filter.Eq(e => e.Index, equipment.Index);
        //    var result = await _collection.ReplaceOneAsync(filter, equipment);
        //    if (result.IsAcknowledged && result.ModifiedCount > 0)
        //        return equipment;

        //    // Could throw or handle not found
        //    throw new KeyNotFoundException($"Equipment with index '{equipment.Index}' not found.");
        //}

        public async Task DeleteByIndex(string index)
        {
            var result = await _collection.DeleteOneAsync(e => e.Index == index);
            if (!result.IsAcknowledged || result.DeletedCount == 0)
                throw new KeyNotFoundException($"Equipment with index '{index}' not found.");
        }

        public async Task<PagedResult<Equipment>> GetAllPaginatedAsync(int page, int pageSize, string? tag = null, string? tier = null, string? damageType = null, string? name = null)
        {
            try
            {
                var builder = Builders<Equipment>.Filter;
                var filter = builder.Eq(e => e.IsDeleted, false);

                if (!string.IsNullOrEmpty(tag)) filter &= builder.AnyEq(e => e.Tags!, tag);
                if (!string.IsNullOrEmpty(tier)) filter &= builder.Eq(e => e.Tier, tier);
                if (!string.IsNullOrEmpty(damageType)) filter &= builder.Eq(e => e.Damage!.DamageType.Name, damageType);
                if (!string.IsNullOrEmpty(name)) filter &= builder.Regex(e => e.Name, new BsonRegularExpression(name, "i"));

                var count = await _collection.CountDocumentsAsync(filter);
                var results = await _collection.Find(filter)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync();

                return new PagedResult<Equipment>
                {
                    Items = results,
                    TotalItems = (int)count,
                    Page = page,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to retrieve paginated equipment");
                return new PagedResult<Equipment>
                {
                    Items = new List<Equipment>(),
                    TotalItems = 0,
                    Page = page,
                    PageSize = pageSize
                };
            }
        }
    }
}
