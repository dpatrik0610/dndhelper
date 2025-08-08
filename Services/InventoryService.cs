using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly IInventoryRepository _repository;
        private readonly ILogger _logger;

        public InventoryService(IInventoryRepository repo, ILogger logger)
        {
            _repository = repo ?? throw new ArgumentNullException(nameof(repo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Helper for argument validation
        private void ValidateId(string id, string paramName)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException($"{paramName} cannot be null or empty");
        }

        // Inventories

        public async Task<IEnumerable<Inventory>> GetInventoriesByCharacterAsync(string characterId)
        {
            ValidateId(characterId, nameof(characterId));
            _logger.Information($"Fetching inventories for character {characterId}");

            try
            {
                var inventories = await _repository.GetByCharacterIdAsync(characterId);
                if (inventories == null)
                {
                    _logger.Warning($"No inventories found for character {characterId}");
                    return Array.Empty<Inventory>();
                }
                return inventories;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error fetching inventories for character {characterId}");
                throw;
            }
        }

        public async Task<Inventory?> GetInventoryByIdAsync(string id)
        {
            ValidateId(id, nameof(id));
            _logger.Information($"Fetching inventory with ID {id}");

            try
            {
                var inventory = await _repository.GetByIdAsync(id);
                if (inventory == null)
                    _logger.Warning($"Inventory not found with ID {id}");
                return inventory;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error fetching inventory with ID {id}");
                throw;
            }
        }

        public async Task<Inventory> CreateInventoryAsync(Inventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            _logger.Information($"Creating inventory for character {inventory.CharacterId}");

            try
            {
                return await _repository.AddAsync(inventory);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error creating inventory for character {inventory.CharacterId}");
                throw;
            }
        }

        public async Task<Inventory?> UpdateInventoryAsync(Inventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            _logger.Information($"Updating inventory with ID {inventory.Id}");

            try
            {
                var updated = await _repository.UpdateAsync(inventory);
                if (updated == null)
                {
                    _logger.Warning($"Inventory with ID {inventory.Id} not found");
                    return null;
                }
                _logger.Information($"Inventory with ID {inventory.Id} updated successfully");
                return updated;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error updating inventory with ID {inventory.Id}");
                throw;
            }
        }

        public async Task DeleteInventoryAsync(string id)
        {
            ValidateId(id, nameof(id));
            _logger.Information($"Deleting inventory with ID {id}");

            try
            {
                await _repository.DeleteAsync(id);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.Warning(ex, $"Delete failed. Inventory not found: {id}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error deleting inventory with ID {id}");
                throw;
            }
        }

        // Inventory Items

        public async Task<IEnumerable<InventoryItem>> GetItemsAsync(string inventoryId)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            _logger.Information($"Fetching items for inventory {inventoryId}");

            try
            {
                var items = await _repository.GetItemsAsync(inventoryId);
                if (items == null)
                {
                    _logger.Warning($"No items found for inventory {inventoryId}");
                    return Array.Empty<InventoryItem>();
                }
                return items;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error fetching items for inventory {inventoryId}");
                throw;
            }
        }

        public async Task<InventoryItem?> GetItemAsync(string inventoryId, string equipmentIndex)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            ValidateId(equipmentIndex, nameof(equipmentIndex));
            _logger.Information($"Fetching item {equipmentIndex} in inventory {inventoryId}");

            try
            {
                var item = await _repository.GetItemAsync(inventoryId, equipmentIndex);
                if (item == null)
                    _logger.Warning($"Item {equipmentIndex} not found in inventory {inventoryId}");
                return item;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error fetching item {equipmentIndex} in inventory {inventoryId}");
                throw;
            }
        }

        public async Task AddItemAsync(string inventoryId, InventoryItem item)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            if (item == null) throw new ArgumentNullException(nameof(item));

            _logger.Information($"Adding item {item.EquipmentIndex} to inventory {inventoryId}");
            try
            {
                var addedItem = await _repository.AddItemAsync(inventoryId, item);
                if (addedItem == null)
                {
                    _logger.Warning($"Failed to add item {item.EquipmentIndex} to inventory {inventoryId}");
                    throw new KeyNotFoundException($"Inventory {inventoryId} not found or item not added.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error adding item {item.EquipmentIndex} to inventory {inventoryId}");
                throw;
            }
        }

        public async Task UpdateItemAsync(string inventoryId, InventoryItem item)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            if (item == null) throw new ArgumentNullException(nameof(item));
            _logger.Information($"Updating item {item.EquipmentIndex} in inventory {inventoryId}");

            try
            {
                await _repository.UpdateItemAsync(inventoryId, item);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error updating item {item.EquipmentIndex} in inventory {inventoryId}");
                throw;
            }
        }

        public async Task DeleteItemAsync(string inventoryId, string equipmentIndex)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            ValidateId(equipmentIndex, nameof(equipmentIndex));
            _logger.Information($"Deleting item {equipmentIndex} from inventory {inventoryId}");

            try
            {
                await _repository.DeleteItemAsync(inventoryId, equipmentIndex);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.Warning(ex, $"Delete failed. Item {equipmentIndex} not found in inventory {inventoryId}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error deleting item {equipmentIndex} from inventory {inventoryId}");
                throw;
            }
        }
    }
}
