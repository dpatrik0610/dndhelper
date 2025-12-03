using dndhelper.Authorization;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.CharacterServices.Interfaces;
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
        private readonly ICharacterService _characterService;

        public InventoryService(
            IInventoryRepository repo, 
            ILogger logger, 
            IEquipmentRepository equipmentRepo,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            ICharacterService characterService
            ) : base(repo, logger, authorizationService, httpContextAccessor) 
        {
            _equipmentRepo = equipmentRepo;
            _characterService = characterService;
        }

        private async Task<List<string>> ResolveOwnerIdsFromCharacterIdsAsync(IEnumerable<string>? characterIds)
        {
            if (characterIds == null)
                return new List<string>();

            var ids = characterIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return new List<string>();

            var characters = await _characterService.GetByIdsAsync(ids);

            return characters
                .Where(c => c.OwnerIds != null)
                .SelectMany(c => c.OwnerIds!)
                .Distinct()
                .ToList();
        }

        public async Task<IEnumerable<Inventory>> GetByCharacterAsync(string characterId)
        {
            Guard.NotNullOrWhiteSpace(characterId, nameof(characterId));
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

        public async Task<IEnumerable<Inventory>> GetFromCharacterInventoryIdsAsync(string characterId)
        {
            Guard.NotNullOrWhiteSpace(characterId, nameof(characterId));
            _logger.Information($"Fetching inventories for character {characterId}");

            try
            {
                var character = await _characterService.GetByIdAsync(characterId);

                Guard.NotNull(character, nameof(character));
                Guard.NotNullOrEmpty(character!.InventoryIds, nameof(character.InventoryIds));

                List<Inventory> response = [];
                foreach(var inventoryId in character.InventoryIds!.ToHashSet())
                {
                    var inventory = await _repository.GetByIdAsync(inventoryId);
                    if (inventory != null)
                    {
                        response.Add(inventory);
                    }
                }
                return response;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error fetching inventories for character {characterId}");
                throw;
            }
        }

        public async Task<IEnumerable<Inventory>> AddInventoryToCharacter(string characterId, string inventoryId)
        {
            Guard.NotNullOrWhiteSpace(characterId, nameof(characterId));
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));

            try
            {
                var character = await _characterService.GetByIdAsync(characterId);
                Guard.NotNull(character, nameof(character));

                if (character!.InventoryIds == null) character.InventoryIds = [];
                if (!character.InventoryIds.Contains(inventoryId)) character.InventoryIds.Add(inventoryId);

                await _characterService.UpdateInternalAsync(character);
                return await GetFromCharacterInventoryIdsAsync(characterId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error adding inventory {inventoryId} for character {characterId}");
                throw;
            }
        }

        public override async Task<Inventory?> CreateAsync(Inventory entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var characterOwners = await ResolveOwnerIdsFromCharacterIdsAsync(entity.CharacterIds);

            entity.OwnerIds ??= new List<string>();

            foreach (var ownerId in characterOwners)
            {
                if (!entity.OwnerIds.Contains(ownerId))
                    entity.OwnerIds.Add(ownerId);
            }

            return await base.CreateAsync(entity);
        }

        public override async Task<Inventory?> UpdateAsync(Inventory entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            if (entity is IOwnedResource owned)
                await EnsureOwnershipAccess(owned);

            var characterOwners = await ResolveOwnerIdsFromCharacterIdsAsync(entity.CharacterIds);

            entity.OwnerIds ??= new List<string>();

            entity.OwnerIds = entity.OwnerIds
                .Concat(characterOwners)
                .Distinct()
                .ToList();

            entity.UpdatedAt = DateTime.UtcNow;

            return await _repository.UpdateAsync(entity);
        }

        // Inventory Items
        public async Task<IEnumerable<InventoryItem>> GetItemsAsync(string inventoryId)
        {
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
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
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
            Guard.NotNullOrWhiteSpace(equipmentId, nameof(equipmentId));
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
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
            Guard.NotNullOrWhiteSpace(equipmentId, nameof(equipmentId));

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
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
            Guard.NotNull(item, nameof(item));

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
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
            Guard.NotNullOrWhiteSpace(equipmentId, nameof(equipmentId));
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
            Guard.NotNull(equipment, nameof(equipment));
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));

            var newItem = await _equipmentRepo.CreateAsync(equipment);
            _logger.Information("Creating new item from " + newItem.Id);

            InventoryItem inventoryItem = new InventoryItem { EquipmentId = equipment.Id, EquipmentName = equipment.Name, Quantity = 1 };
            return await _repository.AddItemAsync(inventoryId, inventoryItem);
        }

        public async Task DecrementItemQuantityAsync(string inventoryId, string equipmentId, int decrementBy = 1)
        {
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
            Guard.NotNullOrWhiteSpace(equipmentId, nameof(equipmentId));
            Guard.GreaterThanZero(decrementBy, nameof(decrementBy));

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
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
            Guard.NotNullOrWhiteSpace(equipmentId, nameof(equipmentId));

            var inventory = await _repository.GetByIdAsync(inventoryId);
            if (inventory is null)
                return false;

            if (inventory.Items.IsNullOrEmpty())
                return false;

            return inventory.Items!.Any(x => x.EquipmentId == equipmentId);
        }

        public async Task MoveItemAsync(string sourceInventoryId, string targetInventoryId, string equipmentId, int amount = 1)
        {
            Guard.NotNullOrWhiteSpace(sourceInventoryId, nameof(sourceInventoryId));
            Guard.NotNullOrWhiteSpace(targetInventoryId, nameof(targetInventoryId));
            Guard.NotNullOrWhiteSpace(equipmentId, nameof(equipmentId));
            Guard.GreaterThanZero(amount, nameof(amount));

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
