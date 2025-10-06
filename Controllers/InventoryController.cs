using dndhelper.Models;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        public InventoryController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        // -------------------
        // Inventory Endpoints
        // -------------------

        [HttpGet("character/{characterId}")]
        public async Task<ActionResult<IEnumerable<Inventory>>> GetInventoriesByCharacter(string characterId)
        {
            try
            {
                var inventories = await _inventoryService.GetByCharacterAsync(characterId);
                return Ok(inventories);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Inventory>> GetInventory(string id)
        {
            try
            {
                var inventory = await _inventoryService.GetByIdAsync(id);
                if (inventory == null) return NotFound();
                return Ok(inventory);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost]
        public async Task<ActionResult<Inventory>> CreateInventory(Inventory inventory)
        {
            try
            {
                var created = await _inventoryService.CreateAsync(inventory);
                return CreatedAtAction(nameof(GetInventory), new { id = created!.Id }, created);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<Inventory>> UpdateInventory(string id, Inventory inventory)
        {
            if (id != inventory.Id)
                return BadRequest("ID mismatch.");

            try
            {
                var updated = await _inventoryService.UpdateAsync(inventory);
                return Ok(updated);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteInventory(string id)
        {
            try
            {
                await _inventoryService.DeleteAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // ----------------------
        // Inventory Item Endpoints
        // ----------------------

        [HttpGet("{inventoryId}/items")]
        public async Task<ActionResult<IEnumerable<InventoryItem>>> GetItems(string inventoryId)
        {
            try
            {
                var items = await _inventoryService.GetItemsAsync(inventoryId);
                return Ok(items);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("{inventoryId}/items/{equipmentIndex}")]
        public async Task<ActionResult<InventoryItem>> GetItem(string inventoryId, string equipmentIndex)
        {
            try
            {
                var item = await _inventoryService.GetItemAsync(inventoryId, equipmentIndex);
                if (item == null) return NotFound();
                return Ok(item);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{inventoryId}/additem")]
        public async Task<IActionResult> AddItem(string inventoryId, InventoryItem item)
        {
            try
            {
                var response = await _inventoryService.AddOrIncrementItemAsync(inventoryId, item);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{inventoryId}/additem/new")]
        public async Task<IActionResult> AddNewItem(string inventoryId, Equipment equipment)
        {
            try
            {
                var item = await _inventoryService.AddNewItemAsync(inventoryId, equipment);
                return Ok(item);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPut("{inventoryId}/items/{equipmentId}")]
        public async Task<IActionResult> UpdateItem(string inventoryId, string equipmentIndex, InventoryItem item)
        {
            if (equipmentIndex != item.EquipmentId)
                return BadRequest("Equipment index mismatch.");

            try
            {
                await _inventoryService.UpdateItemAsync(inventoryId, item);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpDelete("{inventoryId}/items/{equipmentIndex}")]
        public async Task<IActionResult> DeleteItem(string inventoryId, string equipmentIndex)
        {
            try
            {
                await _inventoryService.DeleteItemAsync(inventoryId, equipmentIndex);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // PATCH: api/inventory/{inventoryId}/items/{equipmentId}/decrement?amount=1
        [HttpPatch("{inventoryId}/items/{equipmentId}/decrement")]
        public async Task<IActionResult> DecrementItemQuantity(string inventoryId, string equipmentId, [FromQuery] int amount = 1)
        {
            if (string.IsNullOrWhiteSpace(inventoryId) || string.IsNullOrWhiteSpace(equipmentId))
                return BadRequest("Invalid inventory or equipment ID.");

            if (amount <= 0)
                return BadRequest("Decrement amount must be greater than zero.");

            try
            {
                await _inventoryService.DecrementItemQuantityAsync(inventoryId, equipmentId, amount);
                return Ok(new { message = $"Item {equipmentId} decremented by {amount} in inventory {inventoryId}." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

    }
}
