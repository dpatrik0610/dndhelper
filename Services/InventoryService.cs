using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class InventoryService : BaseService<Inventory, IInventoryRepository>, IInventoryService
    {
        private readonly IEquipmentRepository _equipmentRepo;
        public InventoryService(IInventoryRepository repo, ILogger logger, IEquipmentRepository equipmentRepo, IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor) : base(repo, logger, authorizationService, httpContextAccessor) 
        {
            _equipmentRepo = equipmentRepo;
        }


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

        public async Task<InventoryItem> AddOrIncrementItemAsync(string inventoryId, string equipmentId, int incrementVal = 1)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            if (string.IsNullOrEmpty(equipmentId)) throw new ArgumentNullException(nameof(equipmentId));

            var equipment = await _equipmentRepo.GetByIdAsync(equipmentId);
            if (equipment is null)
                throw new KeyNotFoundException("Equipment not found globally. Use 'Add New Item' option instead.");

            var inventory = await _repository.GetByIdAsync(inventoryId)
                ?? throw new InvalidOperationException($"Inventory {inventoryId} does not exist.");

            _logger.Information($"Adding item {equipmentId} to inventory {inventoryId}");

            try
            {
                // If inventory contains the item, increment quantity by amount.
                var existing = inventory.Items?.Find(x => x.EquipmentId == equipmentId);
                if (existing != null)
                {
                    existing.Quantity += incrementVal;
                    await _repository.UpdateItemAsync(inventoryId, existing);
                    return existing;
                }

                // If inventory does not contain it yet:
                var newInventoryItem = new InventoryItem()
                {
                    EquipmentId = equipment.Id,
                    EquipmentName = equipment.Name,
                    Quantity = incrementVal,
                    Note = "",
                };

                var addedItem = await _repository.AddItemAsync(inventoryId, newInventoryItem!)
                    ?? throw new KeyNotFoundException($"Unkown Error: Item {equipmentId} could not be added to inventory {inventoryId}.");

                return addedItem;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error adding item {equipmentId} to inventory {inventoryId}");
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

        public async Task<InventoryItem?> AddNewItemAsync(string inventoryId, Equipment equipment)
        {
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            if (inventoryId == null) throw new ArgumentNullException(nameof(inventoryId));

            var newItem = await _equipmentRepo.CreateAsync(equipment);
            _logger.Information("Creating new item from " + newItem.Id);

            InventoryItem inventoryItem = new InventoryItem { EquipmentId = equipment.Id, EquipmentName = equipment.Name, Quantity = 1 };
            return await _repository.AddItemAsync(inventoryId, inventoryItem);
        }

        public async Task DecrementItemQuantityAsync(string inventoryId, string equipmentId, int decrementBy = 1)
        {
            ValidateId(inventoryId, nameof(inventoryId));
            ValidateId(equipmentId, nameof(equipmentId));

            if (decrementBy <= 0)
                throw new ArgumentOutOfRangeException(nameof(decrementBy), "Decrement value must be greater than zero.");

            _logger.Information($"Decrementing item {equipmentId} by {decrementBy} in inventory {inventoryId}");

            try
            {
                var inventory = await _repository.GetByIdAsync(inventoryId)
                                ?? throw new KeyNotFoundException($"Inventory {inventoryId} not found.");

                var existing = inventory.Items?.Find(x => x.EquipmentId == equipmentId);
                if (existing == null)
                    throw new KeyNotFoundException($"Item {equipmentId} not found in inventory {inventoryId}.");

                if (existing.Quantity > decrementBy)
                {
                    existing.Quantity -= decrementBy;
                    await _repository.UpdateItemAsync(inventoryId, existing);
                    _logger.Information($"Decremented quantity of {equipmentId} to {existing.Quantity} in inventory {inventoryId}");
                }
                else
                {
                    await _repository.DeleteItemAsync(inventoryId, equipmentId);
                    _logger.Information($"Removed item {equipmentId} from inventory {inventoryId} after reaching 0 or below");
                }
            }
            catch (KeyNotFoundException ex)
            {
                _logger.Warning(ex, $"Decrement failed. Item {equipmentId} not found in inventory {inventoryId}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error decrementing item {equipmentId} in inventory {inventoryId}");
                throw;
            }
        }


        public async Task<bool> ItemExistsInInventoryAsync(string inventoryId, string equipmentId)
        {
            var inventory = await _repository.GetByIdAsync(inventoryId);
            if (inventory is null) return false;
            if (!EnumerableExtensions.IsNullOrEmpty(inventory.Items)) return false;

            return inventory.Items!.Any(x => x.EquipmentId == equipmentId);
            
        }

        public async Task MoveItemAsync(string sourceInventoryId, string targetInventoryId, string equipmentId, int amount = 1)
        {
            ValidateId(sourceInventoryId, nameof(sourceInventoryId));
            ValidateId(targetInventoryId, nameof(targetInventoryId));
            ValidateId(equipmentId, nameof(equipmentId));

            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Move amount must be greater than zero.");

            try
            {
                // Decrement from source inventory
                await DecrementItemQuantityAsync(sourceInventoryId, equipmentId, amount);

                // Add to target inventory (will increment if it already exists)
                await AddOrIncrementItemAsync(targetInventoryId, equipmentId, amount);

                _logger.Information($"Successfully moved {amount} of item {equipmentId} to {targetInventoryId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to move item {equipmentId} from {sourceInventoryId} to {targetInventoryId}");
                throw;
            }
        }

    }
}
