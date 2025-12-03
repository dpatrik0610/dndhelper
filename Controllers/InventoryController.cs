using dndhelper.Authentication.Interfaces;
using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using dndhelper.Services.CharacterServices.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IEntitySyncService _entitySyncService;
    private readonly IAuthService _authService;
    private readonly ICampaignService _campaignService;
    private readonly ICharacterService _characterService;
    private readonly ILogger _logger;

    public InventoryController(
        IInventoryService inventoryService,
        IEntitySyncService entitySyncService,
        IAuthService authService,
        ICampaignService campaignService,
        ICharacterService characterService,
        ILogger logger)
    {
        _inventoryService = inventoryService;
        _entitySyncService = entitySyncService;
        _authService = authService;
        _campaignService = campaignService;
        _characterService = characterService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet("all")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll()
    {
        var inventories = await _inventoryService.GetAllAsync();
        return Ok(inventories ?? []);
    }

    [HttpGet("character/{characterId}")]
    public async Task<IActionResult> GetInventoriesByCharacter(string characterId)
    {
        var inventories = await _inventoryService.GetFromCharacterInventoryIdsAsync(characterId);
        return Ok(inventories);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetInventory(string id)
    {
        var inventory = await _inventoryService.GetByIdAsync(id);
        if (inventory == null) return NotFound();
        return Ok(inventory);
    }

    [HttpPost]
    public async Task<IActionResult> CreateInventory(Inventory inventory)
    {
        var created = await _inventoryService.CreateAsync(inventory);
        if (string.IsNullOrEmpty(created?.Id))
            return StatusCode(500, "Server side error at inventory creation.");

        await BroadcastInventoryChangeAsync(created, "created", created);

        return CreatedAtAction(nameof(GetInventory), "Inventory", new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateInventory(string id, Inventory inventory)
    {
        if (id != inventory.Id)
            return BadRequest("ID mismatch.");

        var updated = await _inventoryService.UpdateAsync(inventory);
        if (updated == null)
            return NotFound();

        await BroadcastInventoryChangeAsync(updated, "updated", updated);

        return Ok(updated);
    }

    [HttpPatch("{inventoryId}/assign-to/{characterId}")]
    public async Task<IActionResult> AddInventoryToCharacter(string characterId, string inventoryId)
    {
        var inventories = await _inventoryService.AddInventoryToCharacter(characterId, inventoryId);

        // Broadcast character change (inventories list updated)
        var character = await _characterService.GetByIdAsync(characterId);
        if (character != null)
        {
            await BroadcastCharacterChangeAsync(character, "updated", new
            {
                characterId = character.Id,
                inventoryIds = character.InventoryIds
            });
        }

        return Ok(inventories);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteInventory(string id)
    {
        var existing = await _inventoryService.GetByIdAsync(id);
        if (existing == null)
            return NotFound();

        var deleted = await _inventoryService.DeleteAsync(id);
        if (!deleted)
            return StatusCode(500, "Error deleting inventory.");

        await BroadcastInventoryChangeAsync(existing, "deleted", data: null);

        return NoContent();
    }

    // ----------------------
    // Inventory Item Endpoints
    // ----------------------

    [HttpGet("{inventoryId}/items")]
    public async Task<IActionResult> GetItems(string inventoryId)
    {
        var items = await _inventoryService.GetItemsAsync(inventoryId);
        return Ok(items);
    }

    [HttpGet("{inventoryId}/items/{equipmentId}")]
    public async Task<IActionResult> GetItem(string inventoryId, string equipmentId)
    {
        var item = await _inventoryService.GetItemAsync(inventoryId, equipmentId);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost("{inventoryId}/additem")]
    public async Task<IActionResult> AddItem(string inventoryId, [FromBody] ModifyItemAmountRequest request)
    {
        var response = await _inventoryService.AddOrIncrementItemAsync(
            inventoryId,
            request.EquipmentId,
            request.Amount
        );

        var inventory = await _inventoryService.GetByIdAsync(inventoryId);
        if (inventory != null)
            await BroadcastInventoryChangeAsync(inventory, "updated", inventory);

        return Ok(response);
    }

    [HttpPost("{inventoryId}/additem/new")]
    public async Task<IActionResult> AddNewItem(string inventoryId, Equipment equipment)
    {
        var item = await _inventoryService.AddNewItemAsync(inventoryId, equipment);

        var inventory = await _inventoryService.GetByIdAsync(inventoryId);
        if (inventory != null)
            await BroadcastInventoryChangeAsync(inventory, "updated", inventory);

        return Ok(item);
    }

    [HttpPost("{sourceInventoryId}/items/{equipmentId}/move")]
    public async Task<IActionResult> MoveItem(string sourceInventoryId, string equipmentId, [FromBody] MoveItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(sourceInventoryId) ||
            string.IsNullOrWhiteSpace(request.TargetInventoryId) ||
            string.IsNullOrWhiteSpace(equipmentId))
            return BadRequest("Invalid inventory or equipment ID.");

        await _inventoryService.MoveItemAsync(
            sourceInventoryId,
            request.TargetInventoryId,
            equipmentId,
            request.Amount
        );

        var source = await _inventoryService.GetByIdInternalAsync(sourceInventoryId);
        var target = await _inventoryService.GetByIdInternalAsync(request.TargetInventoryId);

        if (source != null)
            await BroadcastInventoryChangeAsync(source, "updated", source);

        if (target != null)
            await BroadcastInventoryChangeAsync(target, "updated", target);

        return Ok(new
        {
            message = $"Moved {equipmentId} from {sourceInventoryId} to {request.TargetInventoryId}."
        });
    }

    [HttpPut("{inventoryId}/items/{equipmentId}")]
    public async Task<IActionResult> UpdateItem(string inventoryId, string equipmentId, InventoryItem item)
    {
        if (equipmentId != item.EquipmentId)
            return BadRequest("Equipment index mismatch.");

        await _inventoryService.UpdateItemAsync(inventoryId, item);

        var inventory = await _inventoryService.GetByIdAsync(inventoryId);
        if (inventory != null)
            await BroadcastInventoryChangeAsync(inventory, "updated", inventory);

        return NoContent();
    }

    [HttpDelete("{inventoryId}/items/{equipmentId}")]
    public async Task<IActionResult> DeleteItem(string inventoryId, string equipmentId)
    {
        await _inventoryService.DeleteItemAsync(inventoryId, equipmentId);

        var inventory = await _inventoryService.GetByIdAsync(inventoryId);
        if (inventory != null)
            await BroadcastInventoryChangeAsync(inventory, "updated", inventory);

        return NoContent();
    }

    [HttpPatch("{inventoryId}/items/")]
    public async Task<IActionResult> DecrementItemQuantity(
        string inventoryId,
        [FromBody] ModifyItemAmountRequest request)
    {
        if (string.IsNullOrWhiteSpace(inventoryId) ||
            string.IsNullOrWhiteSpace(request.EquipmentId))
            return BadRequest("Invalid inventory or equipment ID.");

        if (request.Amount <= 0)
            return BadRequest("Decrement amount must be greater than zero.");

        await _inventoryService.DecrementItemQuantityAsync(
            inventoryId,
            request.EquipmentId,
            request.Amount
        );

        var inventory = await _inventoryService.GetByIdAsync(inventoryId);
        if (inventory != null)
            await BroadcastInventoryChangeAsync(inventory, "updated", inventory);

        return Ok(new
        {
            message = $"Item {request.EquipmentId} decremented by {request.Amount} in inventory {inventoryId}."
        });
    }

    public class ModifyItemAmountRequest
    {
        public string EquipmentId { get; set; } = null!;
        public int Amount { get; set; } = 1;
    }

    public class MoveItemRequest
    {
        public string TargetInventoryId { get; set; } = null!;
        public int Amount { get; set; } = 1;
    }

    // =====================
    // SignalR Sync Helper
    // =====================
    private async Task BroadcastInventoryChangeAsync(Inventory inventory, string action, object? data)
    {
        if (inventory.OwnerIds == null || !inventory.OwnerIds.Any())
            return;

        var user = await _authService.GetUserFromTokenAsync();
        var recipients = new HashSet<string>(inventory.OwnerIds);

        // 1) If inventory explicitly has CampaignId → add that campaign's DMs
        if (!string.IsNullOrEmpty(inventory.CampaignId))
        {
            var dmIds = await _campaignService.GetCampaignDMIdsAsync(inventory.CampaignId);
            if (dmIds != null)
            {
                foreach (var dmId in dmIds)
                    recipients.Add(dmId);
            }
        }
        else if (inventory.CharacterIds != null && inventory.CharacterIds.Any())
        {
            // 2) Otherwise derive campaigns from attached characters
            //    (e.g., shared inventory between party members)
            var characters = await _characterService.GetByIdsAsync(inventory.CharacterIds);
            var campaignIds = characters
                .Where(c => !string.IsNullOrEmpty(c.CampaignId))
                .Select(c => c.CampaignId!)
                .Distinct()
                .ToList();

            foreach (var campaignId in campaignIds)
            {
                var dmIds = await _campaignService.GetCampaignDMIdsAsync(campaignId);
                if (dmIds == null) continue;

                foreach (var dmId in dmIds)
                    recipients.Add(dmId);
            }
        }

        await _entitySyncService.BroadcastToUsers(
            "EntityChanged",
            new
            {
                entityType = "Inventory",
                entityId = inventory.Id,
                action,
                data,
                changedBy = user.Username,
                timestamp = DateTime.UtcNow,
            },
            recipients.ToList(),
            excludeUserId: user.Id
        );
    }

    private async Task BroadcastCharacterChangeAsync(Character character, string action, object? data)
    {
        if (character.OwnerIds == null || !character.OwnerIds.Any())
            return;

        var user = await _authService.GetUserFromTokenAsync();
        var recipients = new HashSet<string>(character.OwnerIds);

        if (!string.IsNullOrEmpty(character.CampaignId))
        {
            var dmIds = await _campaignService.GetCampaignDMIdsAsync(character.CampaignId);
            if (dmIds != null)
            {
                foreach (var dmId in dmIds)
                    recipients.Add(dmId);
            }
        }

        await _entitySyncService.BroadcastToUsers(
            "EntityChanged",
            new
            {
                entityType = "Character",
                entityId = character.Id,
                action,
                data,
                changedBy = user.Username,
                timestamp = DateTime.UtcNow,
            },
            recipients.ToList(),
            excludeUserId: user.Id
        );
    }

}
