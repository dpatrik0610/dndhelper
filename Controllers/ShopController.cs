using dndhelper.Authentication.Interfaces;
using dndhelper.Models;
using dndhelper.Models.DTOs;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ShopController : ControllerBase
    {
        private readonly IShopService _shopService;
        private readonly IEntitySyncService _entitySyncService;
        private readonly ILogger _logger;

        public ShopController(
            IShopService shopService,
            IEntitySyncService entitySyncService,
            ILogger logger)
        {
            _shopService = shopService;
            _entitySyncService = entitySyncService;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var shop = await _shopService.GetByIdAsync(id);
            if (shop == null) return NotFound();
            return Ok(shop);
        }

        [HttpGet("campaign/{campaignId}")]
        public async Task<IActionResult> GetShopsForCampaign(string campaignId)
        {
            try
            {
                var shops = await _shopService.GetShopsForCampaignAsync(campaignId);
                return Ok(shops);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateShop([FromBody] Shop shop)
        {
            try
            {
                var created = await _shopService.CreateShopWithInventoryAsync(shop);
                if (created == null) return StatusCode(500, "Failed to create shop.");

                await BroadcastShopChangeAsync(created, "created", created);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{shopId}/items")]
        public async Task<IActionResult> GetShopItems(string shopId)
        {
            try
            {
                var items = await _shopService.GetShopItemsAsync(shopId);
                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get shop items.");
                return StatusCode(500, "An error occurred fetching the shop items.");
            }
        }

        [HttpPost("{id}/buy")]
        public async Task<IActionResult> BuyItem(string id, [FromBody] BuyRequest request)
        {
            try
            {
                var success = await _shopService.BuyItemFromShopAsync(
                    id, 
                    request.BuyerCharacterId, 
                    request.EquipmentId, 
                    request.Quantity
                );

                return Ok(new { success, message = "Purchase complete!" });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpPost("sell-requests")]
        public async Task<IActionResult> SubmitSellRequest([FromBody] SellRequest request)
        {
            try
            {
                var created = await _shopService.SubmitSellRequestAsync(request);
                await _entitySyncService.BroadcastEntityUpdated("SellRequest", created.Id!, created, User.Identity?.Name ?? "Player");
                return Ok(created);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("campaign/{campaignId}/sell-requests")]
        public async Task<IActionResult> GetCampaignSellRequests(string campaignId)
        {
            try
            {
                var requests = await _shopService.GetSellRequestsForCampaignAsync(campaignId);
                return Ok(requests);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPatch("sell-requests/{id}/approve")]
        public async Task<IActionResult> ApproveSellRequest(string id)
        {
            try
            {
                var processed = await _shopService.ProcessSellRequestAsync(id, approve: true);
                if (processed == null) return NotFound();

                await _entitySyncService.BroadcastEntityUpdated("SellRequest", processed.Id!, processed, User.Identity?.Name ?? "DM");
                return Ok(processed);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPatch("sell-requests/{id}/reject")]
        public async Task<IActionResult> RejectSellRequest(string id)
        {
            try
            {
                var processed = await _shopService.ProcessSellRequestAsync(id, approve: false);
                if (processed == null) return NotFound();

                await _entitySyncService.BroadcastEntityUpdated("SellRequest", processed.Id!, processed, User.Identity?.Name ?? "DM");
                return Ok(processed);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateShop(string id, [FromBody] Shop shop)
        {
            if (id != shop.Id) return BadRequest("ID mismatch.");

            var updated = await _shopService.UpdateAsync(shop);
            if (updated == null) return NotFound();

            await BroadcastShopChangeAsync(updated, "updated", updated);
            return Ok(updated);
        }

        [HttpPatch("{id}/toggle-open")]
        public async Task<IActionResult> ToggleShopOpen(string id, [FromQuery] bool isOpen)
        {
            var updated = await _shopService.ToggleShopOpenStatusAsync(id, isOpen);
            if (updated == null) return NotFound();

            await BroadcastShopChangeAsync(updated, "updated", updated);
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteShop(string id)
        {
            var existing = await _shopService.GetByIdAsync(id);
            if (existing == null) return NotFound();

            var deleted = await _shopService.DeleteAsync(id);
            if (!deleted) return StatusCode(500, "An error occurred while deleting the shop.");

            await BroadcastShopChangeAsync(existing, "deleted", null);
            return NoContent();
        }

        private async Task BroadcastShopChangeAsync(Shop shop, string action, Shop? data)
        {
            try
            {
                await _entitySyncService.BroadcastEntityUpdated(
                    entityType: "Shop",
                    entityId: shop.Id ?? string.Empty,
                    entity: data,
                    updatedBy: User.Identity?.Name ?? "System"
                );
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to broadcast Shop update over SignalR. ShopId={ShopId}", shop.Id);
            }
        }
    }
}
