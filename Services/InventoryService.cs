using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class InventoryService : BaseService<Inventory, IInventoryRepository>, IInventoryService
    {
        public InventoryService(IInventoryRepository repo, ILogger logger) : base(repo, logger) { }


        // Helper for argument validation
        private void ValidateId(string id, string paramName)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException($"{paramName} cannot be null or empty");
        }

        // Inventories
        public async Task<IEnumerable<Inventory>> GetByCharacterAsync(string characterId)
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

        public async Task<InventoryItem?> GetItemAsync(string inventoryId, string equipmentId)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            ValidateId(equipmentId, nameof(equipmentId));
            _logger.Information($"Fetching item {equipmentId} in inventory {inventoryId}");

            try
            {
                var item = await _repository.GetItemAsync(inventoryId, equipmentId);
                if (item == null)
                    _logger.Warning($"Item {equipmentId} not found in inventory {inventoryId}");
                return item;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error fetching item {equipmentId} in inventory {inventoryId}");
                throw;
            }
        }

        public async Task AddItemAsync(string inventoryId, InventoryItem item)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            if (item == null) throw new ArgumentNullException(nameof(item));

            _logger.Information($"Adding item {item.EquipmentId} to inventory {inventoryId}");
            try
            {
                var addedItem = await _repository.AddItemAsync(inventoryId, item);
                if (addedItem == null)
                {
                    _logger.Warning($"Failed to add item {item.EquipmentId} to inventory {inventoryId}");
                    throw new KeyNotFoundException($"Inventory {inventoryId} not found or item not added.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error adding item {item.EquipmentId} to inventory {inventoryId}");
                throw;
            }
        }

        public async Task UpdateItemAsync(string inventoryId, InventoryItem item)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            if (item == null) throw new ArgumentNullException(nameof(item));
            _logger.Information($"Updating item {item.EquipmentId} in inventory {inventoryId}");

            try
            {
                await _repository.UpdateItemAsync(inventoryId, item);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error updating item {item.EquipmentId} in inventory {inventoryId}");
                throw;
            }
        }

        public async Task DeleteItemAsync(string inventoryId, string equipmentId)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            ValidateId(equipmentId, nameof(equipmentId));
            _logger.Information($"Deleting item {equipmentId} from inventory {inventoryId}");

            try
            {
                await _repository.DeleteItemAsync(inventoryId, equipmentId);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.Warning(ex, $"Delete failed. Item {equipmentId} not found in inventory {inventoryId}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error deleting item {equipmentId} from inventory {inventoryId}");
                throw;
            }
        }
    }
}
