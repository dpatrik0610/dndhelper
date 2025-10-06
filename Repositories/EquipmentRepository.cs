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

        public new async Task<Equipment> UpdateAsync(Equipment equipment)
        {
            var filter = Builders<Equipment>.Filter.Eq(e => e.Index, equipment.Index);
            var result = await _collection.ReplaceOneAsync(filter, equipment);
            if (result.IsAcknowledged && result.ModifiedCount > 0)
                return equipment;

            // Could throw or handle not found
            throw new KeyNotFoundException($"Equipment with index '{equipment.Index}' not found.");
        }

        public async Task DeleteByIndex(string index)
        {
            var result = await _collection.DeleteOneAsync(e => e.Index == index);
            if (!result.IsAcknowledged || result.DeletedCount == 0)
                throw new KeyNotFoundException($"Equipment with index '{index}' not found.");
        }
    }
}
