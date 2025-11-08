using dndhelper.Authentication;
using dndhelper.Models;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens.Experimental;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CurrencyController : ControllerBase
    {
        private readonly ICurrencyService _currencyService;
        private readonly ILogger _logger;

        public CurrencyController(ICurrencyService currencyService, ILogger logger)
        {
            _currencyService = currencyService;
            _logger = logger;
        }

        // GET: api/currency/{characterId}
        [HttpGet("{characterId}")]
        public async Task<IActionResult> GetCharacterCurrencies(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return BadRequest(new { message = "Character ID is required." });

            try
            {
                var currencies = await _currencyService.GetCharacterCurrenciesAsync(characterId);
                return Ok(new { data = currencies, message = "Currencies retrieved successfully." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting currencies for character {CharacterId}", characterId);
                return StatusCode(500, new { message = "An error occurred while fetching currencies." });
            }
        }

        // PUT: api/currency/remove/{characterId}
        [HttpPut("remove/{characterId}")]
        public async Task<IActionResult> RemoveCurrencies(string characterId, [FromBody] List<Currency> currencies)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return BadRequest(new { message = "Character ID is required." });

            if (currencies == null || currencies.Count == 0)
                return BadRequest(new { message = "No currencies provided." });

            try
            {
                await _currencyService.RemoveCurrencyFromCharacter(characterId, currencies);
                return Ok(new { message = "Currencies removed successfully." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error removing currencies from character {CharacterId}", characterId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // PUT: api/currency/transfer/{targetId}
        [HttpPut("transfer/{targetId}")]
        public async Task<IActionResult> TransferCurrencies(string targetId, [FromBody] List<Currency> currencies)
        {
            if (string.IsNullOrWhiteSpace(targetId))
                return BadRequest(new { message = "Target ID is required." });

            if (currencies == null || currencies.Count == 0)
                return BadRequest(new { message = "No currencies provided." });

            try
            {
                await _currencyService.TransferManyToCharacter(targetId, currencies);
                return Ok(new { message = "Currencies transferred successfully." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error transferring currencies to character {CharacterId}", targetId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // PUT: api/currency/inventory/{inventoryId}
        [Authorize(Roles = "Admin")]
        [HttpPut("inventory/{inventoryId}")]
        public async Task<IActionResult> AddCurrenciesToInventory(string inventoryId, [FromBody] List<Currency> currencies)
        {
            if (string.IsNullOrWhiteSpace(inventoryId))
                return BadRequest("Inventory ID is required.");

            if (currencies == null || currencies.Count == 0)
                return BadRequest("Currency list is required.");

            try
            {
                foreach (var currency in currencies)
                    await _currencyService.AddCurrencyToInventory(inventoryId, currency);

                return Ok(new { message = $"Added {currencies.Count} currencies to inventory {inventoryId}." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error adding currencies to inventory {InventoryId}", inventoryId);
                return StatusCode(500, "An error occurred while adding currencies to inventory.");
            }
        }
    }
}