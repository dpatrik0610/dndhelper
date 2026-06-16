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
        private readonly IShopRepository _shopRepository;

        public InventoryService(
            IInventoryRepository repo, 
            ILogger logger, 
            IEquipmentRepository equipmentRepo,
            IAuthorizationService authorizationService,
            IHttpContextAccessor httpContextAccessor,
            ICharacterService characterService,
            IShopRepository shopRepository
            ) : base(repo, logger, authorizationService, httpContextAccessor) 
        {
            _equipmentRepo = equipmentRepo;
            _characterService = characterService;
            _shopRepository = shopRepository;
        }

        public async Task<bool> IsShopInventoryAsync(string inventoryId)
        {
            if (string.IsNullOrWhiteSpace(inventoryId)) return false;
            var shop = await _shopRepository.GetByInventoryIdAsync(inventoryId);
            return shop != null;
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

            var created = await base.CreateAsync(entity);
            if (created != null && created.CharacterIds != null)
            {
                foreach (var charId in created.CharacterIds)
                {
                    var character = await _characterService.GetByIdAsync(charId);
                    if (character != null)
                    {
                        character.InventoryIds ??= new List<string>();
                        if (!character.InventoryIds.Contains(created.Id!))
                        {
                            character.InventoryIds.Add(created.Id!);
                            await _characterService.UpdateInternalAsync(character);
                        }
                    }
                }
            }
            return created;
        }

        public override async Task<Inventory?> UpdateAsync(Inventory entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            if (entity is IOwnedResource owned)
                await EnsureOwnershipAccess(owned);

            // Get existing before updating to see link differences
            var existing = await _repository.GetByIdAsync(entity.Id!);
            var oldCharIds = existing?.CharacterIds ?? new List<string>();
            var newCharIds = entity.CharacterIds ?? new List<string>();

            var addedCharIds = newCharIds.Except(oldCharIds).ToList();
            var removedCharIds = oldCharIds.Except(newCharIds).ToList();

            var characterOwners = await ResolveOwnerIdsFromCharacterIdsAsync(entity.CharacterIds);

            entity.OwnerIds ??= new List<string>();

            entity.OwnerIds = entity.OwnerIds
                .Concat(characterOwners)
                .Distinct()
                .ToList();

            entity.UpdatedAt = DateTime.UtcNow;

            var updated = await _repository.UpdateAsync(entity);

            if (updated != null)
            {
                // Sync newly added characters
                foreach (var charId in addedCharIds)
                {
                    var character = await _characterService.GetByIdAsync(charId);
                    if (character != null)
                    {
                        character.InventoryIds ??= new List<string>();
                        if (!character.InventoryIds.Contains(updated.Id!))
                        {
                            character.InventoryIds.Add(updated.Id!);
                            await _characterService.UpdateInternalAsync(character);
                        }
                    }
                }

                // Sync newly removed characters
                foreach (var charId in removedCharIds)
                {
                    var character = await _characterService.GetByIdAsync(charId);
                    if (character != null && character.InventoryIds != null)
                    {
                        if (character.InventoryIds.Contains(updated.Id!))
                        {
                            character.InventoryIds.Remove(updated.Id!);
                            await _characterService.UpdateInternalAsync(character);
                        }
                    }
                }
            }

            return updated;
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

            InventoryItem? sourceSnapshot = null;

            try
            {
                var sourceItem = await _repository.GetItemAsync(sourceInventoryId, equipmentId);
                if (sourceItem == null)
                    throw new KeyNotFoundException($"Item {equipmentId} not found in inventory {sourceInventoryId}.");

                sourceSnapshot = new InventoryItem
                {
                    EquipmentId = sourceItem.EquipmentId,
                    EquipmentName = sourceItem.EquipmentName,
                    Quantity = sourceItem.Quantity,
                    Note = sourceItem.Note
                };

                await DecrementItemQuantityAsync(sourceInventoryId, equipmentId, amount);
                await AddOrIncrementItemAsync(targetInventoryId, equipmentId, amount);

                _logger.Information($"Successfully moved {amount} of item {equipmentId} to {targetInventoryId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move item {EquipmentId} from {SourceInventoryId} to {TargetInventoryId}. Error: {ErrorMessage}",
                    equipmentId, sourceInventoryId, targetInventoryId, ex.Message);

                if (sourceSnapshot != null)
                {
                    _logger.Information("Attempting rollback for inventory {InventoryId} and item {EquipmentId}",
                        sourceInventoryId, sourceSnapshot.EquipmentId);
                    await RollbackSourceItemAsync(sourceInventoryId, sourceSnapshot);
                    _logger.Information("Rollback completed for inventory {InventoryId} and item {EquipmentId}",
                        sourceInventoryId, sourceSnapshot.EquipmentId);
                }

                throw;
            }
        }

        public async Task<string> MoveItemToCharacterFirstInventoryAsync(string sourceInventoryId, string characterId, string equipmentId, int amount = 1)
        {
            Guard.NotNullOrWhiteSpace(sourceInventoryId, nameof(sourceInventoryId));
            Guard.NotNullOrWhiteSpace(characterId, nameof(characterId));
            Guard.NotNullOrWhiteSpace(equipmentId, nameof(equipmentId));
            Guard.GreaterThanZero(amount, nameof(amount));

            var character = await _characterService.GetByIdInternalAsync(characterId);
            if (character == null)
                throw new KeyNotFoundException($"Character {characterId} not found.");

            if (character.InventoryIds == null || character.InventoryIds.Count == 0)
                throw new InvalidOperationException("Target character has no inventories.");

            var candidateIds = character.InventoryIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (candidateIds.Count == 0)
                throw new InvalidOperationException("Target character has no valid inventory IDs.");

            string? targetInventoryId = null;
            foreach (var inventoryId in candidateIds)
            {
                var inventory = await _repository.GetByIdAsync(inventoryId);
                if (inventory != null)
                {
                    targetInventoryId = inventoryId;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(targetInventoryId))
                throw new InvalidOperationException($"No valid inventories found for character {characterId}. Inventory IDs: {string.Join(", ", candidateIds)}");

            await MoveItemAsync(sourceInventoryId, targetInventoryId, equipmentId, amount);

            return targetInventoryId;
        }

        private async Task RollbackSourceItemAsync(string inventoryId, InventoryItem snapshot)
        {
            try
            {
                var existing = await _repository.GetItemAsync(inventoryId, snapshot.EquipmentId);
                if (existing != null)
                {
                    existing.Quantity = snapshot.Quantity;
                    existing.EquipmentName = snapshot.EquipmentName;
                    existing.Note = snapshot.Note;
                    await _repository.UpdateItemAsync(inventoryId, existing);
                }
                else
                {
                    await _repository.AddItemAsync(inventoryId, new InventoryItem
                    {
                        EquipmentId = snapshot.EquipmentId,
                        EquipmentName = snapshot.EquipmentName,
                        Quantity = snapshot.Quantity,
                        Note = snapshot.Note
                    });
                }
            }
            catch (Exception rollbackEx)
            {
                _logger.Error(rollbackEx, "Rollback failed for inventory {InventoryId} and item {EquipmentId}. Error: {ErrorMessage}",
                    inventoryId, snapshot.EquipmentId, rollbackEx.Message);
            }
        }

    }
}
