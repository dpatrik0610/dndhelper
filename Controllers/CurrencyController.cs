using dndhelper.Authorization;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    // Validation, not-found, insufficient-funds and access errors are thrown by the service
    // and mapped to 400/403/404 by ExceptionMiddleware.
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CurrencyController : ControllerBase
    {
        private readonly ICurrencyService _currencyService;

        public CurrencyController(ICurrencyService currencyService)
        {
            _currencyService = currencyService;
        }

        [HttpGet("{characterId}")]
        public async Task<IActionResult> GetCharacterCurrencies(string characterId)
        {
            var currencies = await _currencyService.GetCharacterCurrenciesAsync(characterId);
            return Ok(new { data = currencies, message = "Currencies retrieved successfully." });
        }

        [HttpPut("remove/{characterId}")]
        public async Task<IActionResult> RemoveCurrencies(string characterId, [FromBody] List<Currency> currencies)
        {
            await _currencyService.RemoveCurrencyFromCharacterAndNotifyAsync(characterId, currencies);
            return Ok(new { message = "Currencies removed successfully." });
        }

        [HttpPut("transfer/{targetId}")]
        public async Task<IActionResult> TransferCurrencies(string targetId, [FromBody] List<Currency> currencies)
        {
            await _currencyService.TransferManyToCharacterAndNotifyAsync(targetId, currencies);
            return Ok(new { message = "Currencies transferred successfully." });
        }

        // DM hands out money, so only the DM of the inventory's campaign (or the superadmin).
        [HttpPut("inventory/{inventoryId}")]
        public async Task<IActionResult> AddCurrenciesToInventory(
            string inventoryId,
            [FromBody] List<Currency> currencies,
            [FromServices] CampaignAccess access,
            [FromServices] IInventoryRepository inventories)
        {
            await EnsureInventoryDmAsync(inventoryId, access, inventories);
            await _currencyService.AddCurrenciesToInventoryAndNotifyAsync(inventoryId, currencies);
            return Ok(new { message = "Currencies added to inventory." });
        }

        [HttpPut("inventory/{inventoryId}/remove")]
        public async Task<IActionResult> RemoveCurrenciesFromInventory(
            string inventoryId,
            [FromBody] List<Currency> currencies,
            [FromServices] CampaignAccess access,
            [FromServices] IInventoryRepository inventories)
        {
            await EnsureInventoryDmAsync(inventoryId, access, inventories);
            await _currencyService.RemoveCurrenciesFromInventoryAndNotifyAsync(inventoryId, currencies);
            return Ok(new { message = "Currencies removed from inventory." });
        }

        [Authorize(Roles = "Admin, User")]
        [HttpPut("transfer-between/{fromId}/{toId}")]
        public async Task<IActionResult> TransferBetweenCharacters(string fromId, string toId, [FromBody] List<Currency> currencies)
        {
            await _currencyService.TransferBetweenCharactersAndNotifyAsync(fromId, toId, currencies);
            return Ok(new { message = "Currencies transferred successfully." });
        }

        [Authorize(Roles = "Admin, User")]
        [HttpPut("claim/{characterId}/{inventoryId}")]
        public async Task<IActionResult> ClaimFromInventory(string characterId, string inventoryId, [FromBody] List<Currency> currencies)
        {
            await _currencyService.ClaimFromInventoryAndNotifyAsync(characterId, inventoryId, currencies);
            return Ok(new { message = "Currencies claimed successfully." });
        }

        private static async Task EnsureInventoryDmAsync(string inventoryId, CampaignAccess access, IInventoryRepository inventories)
        {
            var inventory = await inventories.GetByIdAsync(inventoryId)
                ?? throw new NotFoundException("Inventory not found.");
            if (inventory.CampaignId == null ? !access.IsSuperAdmin : !await access.IsDmAsync(inventory.CampaignId))
                throw new ForbiddenException("Only the campaign's DM can change an inventory's money.");
        }
    }
}
