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
    public class InventoryRepository : MongoRepository<Inventory>, IInventoryRepository
    {
        public InventoryRepository(MongoDbContext context, ILogger logger, IMemoryCache cache) : base(logger, cache, context, "Inventories") { }

        public async Task<IEnumerable<Inventory>> GetByCharacterIdAsync(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException($"Character ID must not be empty. Provided value: '{characterId}'", nameof(characterId));

            try
            {
                return await _collection.Find(i => i.CharacterId == characterId).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in GetByCharacterIdAsync for CharacterId: {characterId}");
                throw new ApplicationException($"Failed to get inventories by character ID: {characterId} | Exception: {ex.Message}", ex);
            }
        }

        // InventoryItem CRUD

        public async Task<IEnumerable<InventoryItem>> GetItemsAsync(string inventoryId)
        {
            if (string.IsNullOrWhiteSpace(inventoryId))
                throw new ArgumentException($"Inventory ID must not be empty. Provided value: '{inventoryId}'", nameof(inventoryId));

            try
            {
                var inventory = await _collection.Find(i => i.Id == inventoryId).FirstOrDefaultAsync();
                return inventory?.Items ?? Enumerable.Empty<InventoryItem>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in GetItemsAsync for InventoryId: {inventoryId}");
                throw new ApplicationException($"Failed to get inventory items for InventoryId: {inventoryId} | Exception: {ex.Message}", ex);
            }
        }

        public async Task<InventoryItem?> GetItemAsync(string inventoryId, string equipmentId)
        {
            if (string.IsNullOrWhiteSpace(inventoryId))
                throw new ArgumentException($"Inventory ID must not be empty. Provided value: '{inventoryId}'", nameof(inventoryId));
            if (string.IsNullOrWhiteSpace(equipmentId))
                throw new ArgumentException($"EquipmentId must not be empty. Provided value: '{equipmentId}'", nameof(equipmentId));

            try
            {
                var inventory = await _collection.Find(i => i.Id == inventoryId).FirstOrDefaultAsync();
                return inventory?.Items?.FirstOrDefault(item => item.EquipmentId == equipmentId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in GetItemAsync for InventoryId: {inventoryId}, EquipmentId: {equipmentId}");
                throw new ApplicationException($"Failed to get inventory item for InventoryId: {inventoryId}, EquipmentId: {equipmentId} | Exception: {ex.Message}", ex);
            }
        }

        public async Task<InventoryItem?> AddItemAsync(string inventoryId, InventoryItem item)
        {
            if (string.IsNullOrWhiteSpace(inventoryId))
                throw new ArgumentException($"Inventory ID must not be empty. Provided value: '{inventoryId}'", nameof(inventoryId));
            if (item == null)
                throw new ArgumentNullException(nameof(item), $"InventoryItem object is null.");

            try
            {
                if (!ObjectId.TryParse(inventoryId, out var objId))
                    return null;

                var filter = Builders<Inventory>.Filter.Eq("_id", objId);
                var update = Builders<Inventory>.Update.Push(i => i.Items, item);

                var result = await _collection.UpdateOneAsync(filter, update);
                if (result.ModifiedCount == 0)
                    return null;

                // Update cache
                if (_cache.TryGetValue(inventoryId, out Inventory? cachedInventory))
                {
                    cachedInventory.Items ??= new List<InventoryItem>();
                    cachedInventory.Items.Add(item);
                    _cache.Set(inventoryId, cachedInventory);
                }

                var inventory = await _collection.Find(filter).FirstOrDefaultAsync();
                return inventory?.Items?.Find(i => i.EquipmentId == item.EquipmentId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in AddItemAsync for InventoryId: {inventoryId}, Item: {item}");
                throw new ApplicationException($"Failed to add inventory item for InventoryId: {inventoryId}, Item: {item} | Exception: {ex.Message}", ex);
            }
        }

        public async Task UpdateItemAsync(string inventoryId, InventoryItem item)
        {
            if (string.IsNullOrWhiteSpace(inventoryId))
                throw new ArgumentException($"Inventory ID must not be empty. Provided value: '{inventoryId}'", nameof(inventoryId));
            if (item == null)
                throw new ArgumentNullException(nameof(item), $"InventoryItem object is null.");
            if (string.IsNullOrWhiteSpace(item.EquipmentId))
                throw new ArgumentException($"EquipmentId must not be empty. Provided value: '{item.EquipmentId}'", nameof(item.EquipmentId));

            try
            {
                var filter = Builders<Inventory>.Filter.And(
                    Builders<Inventory>.Filter.Eq(i => i.Id, inventoryId),
                    Builders<Inventory>.Filter.ElemMatch(i => i.Items, x => x.EquipmentId == item.EquipmentId)
                );

                var update = Builders<Inventory>.Update.Set("Items.$.Quantity", item.Quantity);

                await _collection.UpdateOneAsync(filter, update);

                // Update cache
                if (_cache.TryGetValue(inventoryId, out Inventory? cachedInventory))
                {
                    var existing = cachedInventory.Items?.FirstOrDefault(i => i.EquipmentId == item.EquipmentId);
                    if (existing != null)
                        existing.Quantity = item.Quantity;

                    _cache.Set(inventoryId, cachedInventory);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in UpdateItemAsync for InventoryId: {inventoryId}, Item: {item}");
                throw new ApplicationException($"Failed to update inventory item for InventoryId: {inventoryId}, Item: {item} | Exception: {ex.Message}", ex);
            }
        }

        public async Task DeleteItemAsync(string inventoryId, string equipmentId)
        {
            if (string.IsNullOrWhiteSpace(inventoryId))
                throw new ArgumentException($"Inventory ID must not be empty. Provided value: '{inventoryId}'", nameof(inventoryId));
            if (string.IsNullOrWhiteSpace(equipmentId))
                throw new ArgumentException($"EquipmentId must not be empty. Provided value: '{equipmentId}'", nameof(equipmentId));

            try
            {
                var update = Builders<Inventory>.Update.PullFilter(i => i.Items, x => x.EquipmentId == equipmentId);
                await _collection.UpdateOneAsync(i => i.Id == inventoryId, update);

                // Update cache
                if (_cache.TryGetValue(inventoryId, out Inventory? cachedInventory))
                {
                    cachedInventory.Items = cachedInventory.Items?.Where(i => i.EquipmentId != equipmentId).ToList();
                    _cache.Set(inventoryId, cachedInventory);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in DeleteItemAsync for InventoryId: {inventoryId}, EquipmentId: {equipmentId}");
                throw new ApplicationException($"Failed to delete inventory item for InventoryId: {inventoryId}, EquipmentId: {equipmentId} | Exception: {ex.Message}", ex);
            }
        }
    }
}
