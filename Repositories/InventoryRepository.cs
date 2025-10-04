using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class InventoryRepository : IInventoryRepository
    {
        private const string InventoryCollectionName = "Inventories";
        private readonly IMongoCollection<Inventory> _collection;
        private readonly ILogger _logger;

        public InventoryRepository(MongoDbContext context, ILogger logger)
        {
            try
            {
                _collection = context.GetCollection<Inventory>(InventoryCollectionName) 
                    ?? throw new ArgumentNullException(nameof(context), $"Failed to load Inventory collection from context: {context}");
                _logger = logger 
                    ?? throw new ArgumentNullException(nameof(logger), $"Logger cannot be null for context: {context}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to initialize InventoryRepository: {ex.Message}");
                throw new ApplicationException($"Failed to initialize InventoryRepository: {ex.Message} | Exception: {ex}");
            }
        }

        // Inventory CRUD

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

        public async Task<Inventory?> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException($"Inventory ID must not be empty. Provided value: '{id}'", nameof(id));

            try
            {
                return await _collection.Find(i => i.Id == id).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in GetByIdAsync for Id: {id}");
                throw new ApplicationException($"Failed to get inventory by ID: {id} | Exception: {ex.Message}", ex);
            }
        }

        public async Task<Inventory> AddAsync(Inventory inventory)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory), $"Inventory object is null.");

            try
            {
                await _collection.InsertOneAsync(inventory);
                return inventory;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in AddAsync for Inventory: {inventory}");
                throw new ApplicationException($"Failed to add inventory: {inventory} | Exception: {ex.Message}", ex);
            }
        }

        public async Task<Inventory?> UpdateAsync(Inventory inventory)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory), $"Inventory object is null.");
            if (string.IsNullOrWhiteSpace(inventory.Id))
                throw new ArgumentException($"Inventory ID must not be empty. Provided value: '{inventory.Id}'", nameof(inventory.Id));

            try
            {
                var filter = Builders<Inventory>.Filter.Eq(x => x.Id, inventory.Id);
                var result = await _collection.ReplaceOneAsync(filter, inventory);

                if (result.MatchedCount == 0)
                    return null;

                return inventory;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in UpdateAsync for Inventory: {inventory}");
                throw new ApplicationException($"Failed to update inventory: {inventory} | Exception: {ex.Message}", ex);
            }
        }

        public async Task DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException($"Inventory ID must not be empty. Provided value: '{id}'", nameof(id));

            try
            {
                await _collection.DeleteOneAsync(i => i.Id == id);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in DeleteAsync for Id: {id}");
                throw new ApplicationException($"Failed to delete inventory with ID: {id} | Exception: {ex.Message}", ex);
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

        public async Task<InventoryItem?> GetItemAsync(string inventoryId, string equipmentIndex)
        {
            if (string.IsNullOrWhiteSpace(inventoryId))
                throw new ArgumentException($"Inventory ID must not be empty. Provided value: '{inventoryId}'", nameof(inventoryId));
            if (string.IsNullOrWhiteSpace(equipmentIndex))
                throw new ArgumentException($"Equipment Index must not be empty. Provided value: '{equipmentIndex}'", nameof(equipmentIndex));

            try
            {
                var inventory = await _collection.Find(i => i.Id == inventoryId).FirstOrDefaultAsync();
                return inventory?.Items?.FirstOrDefault(item => item.EquipmentIndex == equipmentIndex);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in GetItemAsync for InventoryId: {inventoryId}, EquipmentIndex: {equipmentIndex}");
                throw new ApplicationException($"Failed to get inventory item for InventoryId: {inventoryId}, EquipmentIndex: {equipmentIndex} | Exception: {ex.Message}", ex);
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

                var inventory = await _collection.Find(filter).FirstOrDefaultAsync();
                return inventory?.Items?.Find(i => i.EquipmentIndex == item.EquipmentIndex);
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
            if (string.IsNullOrWhiteSpace(item.EquipmentIndex))
                throw new ArgumentException($"Equipment Index must not be empty. Provided value: '{item.EquipmentIndex}'", nameof(item.EquipmentIndex));

            try
            {
                var filter = Builders<Inventory>.Filter.And(
                    Builders<Inventory>.Filter.Eq(i => i.Id, inventoryId),
                    Builders<Inventory>.Filter.ElemMatch(i => i.Items, x => x.EquipmentIndex == item.EquipmentIndex)
                );

                var update = Builders<Inventory>.Update
                    .Set("Items.$.Quantity", item.Quantity)
                    .Set("Items.$.Note", item.Note);

                await _collection.UpdateOneAsync(filter, update);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in UpdateItemAsync for InventoryId: {inventoryId}, Item: {item}");
                throw new ApplicationException($"Failed to update inventory item for InventoryId: {inventoryId}, Item: {item} | Exception: {ex.Message}", ex);
            }
        }

        public async Task DeleteItemAsync(string inventoryId, string equipmentIndex)
        {
            if (string.IsNullOrWhiteSpace(inventoryId))
                throw new ArgumentException($"Inventory ID must not be empty. Provided value: '{inventoryId}'", nameof(inventoryId));
            if (string.IsNullOrWhiteSpace(equipmentIndex))
                throw new ArgumentException($"Equipment Index must not be empty. Provided value: '{equipmentIndex}'", nameof(equipmentIndex));

            try
            {
                var update = Builders<Inventory>.Update.PullFilter(i => i.Items, x => x.EquipmentIndex == equipmentIndex);
                await _collection.UpdateOneAsync(i => i.Id == inventoryId, update);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error in DeleteItemAsync for InventoryId: {inventoryId}, EquipmentIndex: {equipmentIndex}");
                throw new ApplicationException($"Failed to delete inventory item for InventoryId: {inventoryId}, EquipmentIndex: {equipmentIndex} | Exception: {ex.Message}", ex);
            }
        }
    }
}
