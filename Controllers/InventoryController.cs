using dndhelper.Models;
using dndhelper.Services.Interfaces;
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
        private readonly IInventoryService _service;

        public InventoryController(IInventoryService service)
        {
            _service = service;
        }

        // -------------------
        // Inventory Endpoints
        // -------------------

        [HttpGet("character/{characterId}")]
        public async Task<ActionResult<IEnumerable<Inventory>>> GetInventoriesByCharacter(string characterId)
        {
            try
            {
                var inventories = await _service.GetByCharacterAsync(characterId);
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
                var inventory = await _service.GetByIdAsync(id);
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
                var created = await _service.CreateAsync(inventory);
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
                var updated = await _service.UpdateAsync(inventory);
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
                await _service.DeleteAsync(id);
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
                var items = await _service.GetItemsAsync(inventoryId);
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
                var item = await _service.GetItemAsync(inventoryId, equipmentIndex);
                if (item == null) return NotFound();
                return Ok(item);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{inventoryId}/items")]
        public async Task<IActionResult> AddItem(string inventoryId, InventoryItem item)
        {
            try
            {
                await _service.AddItemAsync(inventoryId, item);
                return NoContent();
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
                await _service.UpdateItemAsync(inventoryId, item);
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
                await _service.DeleteItemAsync(inventoryId, equipmentIndex);
                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
