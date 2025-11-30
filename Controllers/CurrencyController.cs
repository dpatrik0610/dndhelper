using dndhelper.Authentication;
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

namespace dndhelper.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CurrencyController : ControllerBase
    {
        private readonly ICurrencyService _currencyService;
        private readonly ILogger _logger;
        private readonly IEntitySyncService _entitySyncService;
        private readonly ICharacterService _characterService;
        private readonly IInventoryService _inventoryService;
        private readonly IAuthService _authService;
        private readonly ICampaignService _campaignService;

        public CurrencyController(
            ICurrencyService currencyService,
            ILogger logger,
            IEntitySyncService entitySyncService,
            ICharacterService characterService,
            IInventoryService inventoryService,
            IAuthService authService,
            ICampaignService campaignService)
        {
            _currencyService = currencyService;
            _logger = logger;
            _entitySyncService = entitySyncService;
            _characterService = characterService;
            _inventoryService = inventoryService;
            _authService = authService;
            _campaignService = campaignService;
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

                var character = await _characterService.GetByIdAsync(characterId);
                if (character != null)
                {
                    await BroadcastCharacterChangeAsync(character, "updated", character);
                }

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

                var target = await _characterService.GetByIdAsync(targetId);
                if (target != null)
                {
                    await BroadcastCharacterChangeAsync(target, "updated", target);
                }

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

                var inventory = await _inventoryService.GetByIdAsync(inventoryId);
                if (inventory != null)
                {
                    await BroadcastInventoryChangeAsync(inventory, "updated", inventory);
                }

                return Ok(new { message = $"Added {currencies.Count} currencies to inventory {inventoryId}." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error adding currencies to inventory {InventoryId}", inventoryId);
                return StatusCode(500, "An error occurred while adding currencies to inventory.");
            }
        }

        [Authorize(Roles = "Admin, User")]
        [HttpPut("transfer-between/{fromId}/{toId}")]
        public async Task<IActionResult> TransferBetweenCharacters(string fromId, string toId, [FromBody] List<Currency> currencies)
        {
            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                return BadRequest(new { message = "Source and target IDs are required." });

            if (currencies == null || currencies.Count == 0)
                return BadRequest(new { message = "No currencies provided." });

            try
            {
                await _currencyService.TransferBetweenCharacters(fromId, toId, currencies);

                var fromCharacter = await _characterService.GetByIdAsync(fromId);
                var toCharacter = await _characterService.GetByIdAsync(toId);

                if (fromCharacter != null)
                    await BroadcastCharacterChangeAsync(fromCharacter, "updated", fromCharacter);

                if (toCharacter != null)
                    await BroadcastCharacterChangeAsync(toCharacter, "updated", toCharacter);

                return Ok(new { message = "Currencies transferred successfully." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error transferring currencies from {FromId} to {ToId}", fromId, toId);
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // PUT: api/currency/claim/{characterId}/{inventoryId}
        [Authorize(Roles = "Admin, User")]
        [HttpPut("claim/{characterId}/{inventoryId}")]
        public async Task<IActionResult> ClaimFromInventory(
            string characterId,
            string inventoryId,
            [FromBody] List<Currency> currencies)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return BadRequest(new { message = "Character ID is required." });

            if (string.IsNullOrWhiteSpace(inventoryId))
                return BadRequest(new { message = "Inventory ID is required." });

            if (currencies == null || currencies.Count == 0)
                return BadRequest(new { message = "No currencies provided." });

            try
            {
                await _currencyService.ClaimFromInventory(characterId, inventoryId, currencies);

                var character = await _characterService.GetByIdAsync(characterId);
                var inventory = await _inventoryService.GetByIdAsync(inventoryId);

                if (character != null)
                    await BroadcastCharacterChangeAsync(character, "updated", character);

                if (inventory != null)
                    await BroadcastInventoryChangeAsync(inventory, "updated", inventory);

                return Ok(new
                {
                    message = $"Claimed {currencies.Count} currencies from inventory {inventoryId} to character {characterId}."
                });
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Error claiming currencies from inventory {InventoryId} to character {CharacterId}",
                    inventoryId,
                    characterId);

                return StatusCode(500, new { message = ex.Message });
            }
        }

        // =====================
        // SignalR helpers
        // =====================

        private async Task BroadcastCharacterChangeAsync(Character character, string action, object data)
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
                recipients.ToList()
            );
        }

        private async Task BroadcastInventoryChangeAsync(Inventory inventory, string action, object data)
        {
            if (inventory.OwnerIds == null || !inventory.OwnerIds.Any())
                return;

            var user = await _authService.GetUserFromTokenAsync();
            var recipients = new HashSet<string>(inventory.OwnerIds);

            // 1) If inventory has CampaignId, add that campaign's DMs
            if (!string.IsNullOrEmpty(inventory.CampaignId))
            {
                var dmIds = await _campaignService.GetCampaignDMIdsAsync(inventory.CampaignId);
                if (dmIds != null)
                {
                    foreach (var dmId in dmIds)
                        recipients.Add(dmId);
                }
            }
            // 2) Otherwise, derive campaigns from attached characters
            else if (inventory.CharacterIds != null && inventory.CharacterIds.Any())
            {
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
    }
}
