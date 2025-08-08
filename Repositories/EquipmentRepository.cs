using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class EquipmentRepository : IEquipmentRepository
    {
        private readonly IMongoCollection<Equipment> _collection;
        private readonly ILogger _logger;
        public EquipmentRepository(MongoDbContext context, ILogger logger)
        {
            _collection = context.GetCollection<Equipment>("Equipment");
            _logger = logger;
        }

        public async Task<IEnumerable<Equipment>> GetEquipmentAsync() =>
            await _collection.Find(_ => true).ToListAsync();

        public async Task<Equipment?> GetEquipmentByIdAsync(string id) =>
            await _collection.Find(e => e.Index == id).FirstOrDefaultAsync();

        public async Task<Equipment> AddEquipmentAsync(Equipment equipment)
        {
            await _collection.InsertOneAsync(equipment);
            return equipment;
        }

        public async Task<bool> AddMultipleEquipmentAsync(IEnumerable<Equipment> equipments)
        {
            try
            {
                await _collection.InsertManyAsync(equipments);
            }
            catch (Exception ex)
            {
                _logger.Error($"There was an error adding multiple equipments: {ex}");
                return false;
            }
            return true;
        }

        public async Task<Equipment?> GetEquipmentByIndexAsync(string index) =>
            await _collection.Find(e => e.Index == index).FirstOrDefaultAsync();

        public async Task<Equipment> UpdateEquipmentAsync(Equipment equipment)
        {
            var filter = Builders<Equipment>.Filter.Eq(e => e.Index, equipment.Index);
            var result = await _collection.ReplaceOneAsync(filter, equipment);
            if (result.IsAcknowledged && result.ModifiedCount > 0)
                return equipment;

            // Could throw or handle not found
            throw new KeyNotFoundException($"Equipment with index '{equipment.Index}' not found.");
        }

        public async Task DeleteEquipmentAsync(string index)
        {
            var result = await _collection.DeleteOneAsync(e => e.Index == index);
            if (!result.IsAcknowledged || result.DeletedCount == 0)
                throw new KeyNotFoundException($"Equipment with index '{index}' not found.");
        }
    }
}
