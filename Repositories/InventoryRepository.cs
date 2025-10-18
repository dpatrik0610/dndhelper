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
        public InventoryRepository(MongoDbContext context, ILogger logger, IMemoryCache cache)
            : base(logger, cache, context, "Inventories") { }

        #region Helper Methods

        private async Task<Inventory?> GetInventoryAsync(string inventoryId)
        {
            if (string.IsNullOrWhiteSpace(inventoryId))
                throw new ArgumentException("Inventory ID cannot be null or empty", nameof(inventoryId));

            // Use base cache helper
            var cached = GetFromCache(inventoryId);
            if (cached != null) return cached;

            var inventory = await _collection.Find(i => i.Id == inventoryId).FirstOrDefaultAsync();
            if (inventory != null)
                AddToCache(inventory);

            return inventory;
        }

        #endregion

        #region Inventory CRUD

        public async Task<IEnumerable<Inventory>> GetByCharacterIdAsync(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("Character ID must not be empty", nameof(characterId));

            try
            {
                var inventories = await _collection.Find(i => i.CharacterId == characterId).ToListAsync();

                // Cache all fetched inventories
                foreach (var inv in inventories)
                    AddToCache(inv);

                return inventories;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in GetByCharacterIdAsync for CharacterId: {CharacterId}", characterId);
                throw new ApplicationException($"Failed to get inventories by character ID: {characterId}", ex);
            }
        }

        #endregion

        #region InventoryItem CRUD

        public async Task<IEnumerable<InventoryItem>> GetItemsAsync(string inventoryId)
        {
            var inventory = await GetInventoryAsync(inventoryId);
            return inventory?.Items ?? Enumerable.Empty<InventoryItem>();
        }

        public async Task<InventoryItem?> GetItemAsync(string inventoryId, string equipmentId)
        {
            if (string.IsNullOrWhiteSpace(equipmentId))
                throw new ArgumentException("EquipmentId must not be empty", nameof(equipmentId));

            var inventory = await GetInventoryAsync(inventoryId);
            return inventory?.Items?.FirstOrDefault(i => i.EquipmentId == equipmentId);
        }

        public async Task<InventoryItem?> AddItemAsync(string inventoryId, InventoryItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var inventory = await GetInventoryAsync(inventoryId);
            if (inventory == null) return null;

            try
            {
                var objId = ObjectId.Parse(inventoryId);
                var filter = Builders<Inventory>.Filter.Eq("_id", objId);
                var update = Builders<Inventory>.Update.Push(i => i.Items, item);

                var result = await _collection.UpdateOneAsync(filter, update);
                if (result.ModifiedCount == 0) return null;

                // Update cache via base class
                inventory.Items ??= new List<InventoryItem>();
                inventory.Items.Add(item);
                UpdateCache(inventory);

                return inventory.Items.FirstOrDefault(i => i.EquipmentId == item.EquipmentId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AddItemAsync for InventoryId: {InventoryId}, Item: {Item}", inventoryId, item);
                throw new ApplicationException($"Failed to add inventory item for InventoryId: {inventoryId}", ex);
            }
        }

        public async Task UpdateItemAsync(string inventoryId, InventoryItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.EquipmentId))
                throw new ArgumentException("Item and EquipmentId must not be null");

            var inventory = await GetInventoryAsync(inventoryId);
            if (inventory == null) return;

            try
            {
                var filter = Builders<Inventory>.Filter.And(
                    Builders<Inventory>.Filter.Eq(i => i.Id, inventoryId),
                    Builders<Inventory>.Filter.ElemMatch(i => i.Items, x => x.EquipmentId == item.EquipmentId)
                );
                var update = Builders<Inventory>.Update.Set("Items.$.Quantity", item.Quantity);

                await _collection.UpdateOneAsync(filter, update);

                // Update cache via base class
                var existing = inventory.Items?.FirstOrDefault(i => i.EquipmentId == item.EquipmentId);
                if (existing != null) existing.Quantity = item.Quantity;
                UpdateCache(inventory);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in UpdateItemAsync for InventoryId: {InventoryId}, Item: {Item}", inventoryId, item);
                throw new ApplicationException($"Failed to update inventory item for InventoryId: {inventoryId}", ex);
            }
        }

        public async Task DeleteItemAsync(string inventoryId, string equipmentId)
        {
            if (string.IsNullOrWhiteSpace(equipmentId))
                throw new ArgumentException("EquipmentId must not be empty", nameof(equipmentId));

            var inventory = await GetInventoryAsync(inventoryId);
            if (inventory == null) return;

            try
            {
                var update = Builders<Inventory>.Update.PullFilter(i => i.Items, x => x.EquipmentId == equipmentId);
                await _collection.UpdateOneAsync(i => i.Id == inventoryId, update);

                // Update cache via base class
                inventory.Items = inventory.Items?.Where(i => i.EquipmentId != equipmentId).ToList();
                UpdateCache(inventory);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in DeleteItemAsync for InventoryId: {InventoryId}, EquipmentId: {EquipmentId}", inventoryId, equipmentId);
                throw new ApplicationException($"Failed to delete inventory item for InventoryId: {inventoryId}", ex);
            }
        }

        #endregion
    }
}
