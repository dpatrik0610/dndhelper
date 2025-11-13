using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using dndhelper.Services.CharacterServices.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class CurrencyService : ICurrencyService
    {
        private readonly ICharacterService _characterService;
        private readonly IInventoryService _inventoryService;
        private readonly ILogger _logger;
        private readonly ClaimsPrincipal _user;

        public CurrencyService(
            ICharacterService characterService,
            IInventoryService inventoryService,
            ILogger logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _characterService = characterService ?? throw new ArgumentNullException(nameof(characterService));
            _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _user = httpContextAccessor?.HttpContext?.User ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        // ----------------------------
        // ADD TO INVENTORY (MERGED)
        // ----------------------------
        public async Task AddCurrencyToInventory(string inventoryId, Currency currency)
        {
            if (string.IsNullOrWhiteSpace(inventoryId))
                throw new ArgumentNullException(nameof(inventoryId));

            var inventory = await _inventoryService.GetByIdAsync(inventoryId);
            if (inventory == null)
                CustomExceptions.ThrowNotFoundException(_logger, "Inventory not found.");

            inventory.Currencies ??= new List<Currency>();
            inventory.Currencies = MergeCurrencies(inventory.Currencies, new[] { currency });

            await _inventoryService.UpdateAsync(inventory);
        }

        // ----------------------------
        // READ CURRENCIES
        // ----------------------------
        public async Task<List<Currency>> GetCharacterCurrenciesAsync(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return new List<Currency>();

            var character = await _characterService.GetByIdAsync(characterId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Character not found.");

            await EnsureCharacterOwnershipAsync(character);
            return character.Currencies ?? new List<Currency>();
        }

        // ----------------------------
        // REMOVE CURRENCIES
        // ----------------------------
        public async Task RemoveCurrencyFromCharacter(string characterId, List<Currency> currencies)
        {
            if (string.IsNullOrWhiteSpace(characterId)) return;

            try
            {
                var character = await _characterService.GetByIdAsync(characterId);
                if (character?.Currencies == null) return;

                await EnsureCharacterOwnershipAsync(character);

                foreach (var currency in currencies)
                    UpdateCurrencyList(character.Currencies, currency, isAddition: false);

                character.Currencies.RemoveAll(c => c.Amount <= 0);
                await _characterService.UpdateAsync(character);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error removing currencies from character {CharacterId}", characterId);
                throw;
            }
        }

        // ----------------------------
        // TRANSFER CURRENCIES
        // ----------------------------
        public async Task TransferManyToCharacter(string targetId, List<Currency> currencies)
        {
            if (string.IsNullOrWhiteSpace(targetId)) return;

            try
            {
                var character = await _characterService.GetByIdAsync(targetId);
                if (character == null) return;

                await EnsureCharacterOwnershipAsync(character);

                character.Currencies ??= new List<Currency>();
                character.Currencies = MergeCurrencies(character.Currencies, currencies);

                await _characterService.UpdateAsync(character);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error transferring currencies to character {CharacterId}", targetId);
                throw;
            }
        }

        public async Task TransferBetweenCharacters(string fromId, string toId, List<Currency> currencies)
        {
            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                throw new ArgumentNullException("Character IDs cannot be null.");

            if (currencies == null || currencies.Count == 0)
                throw new ArgumentException("No currencies provided.");

            try
            {
                var source = await _characterService.GetByIdAsync(fromId)
                    ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Source character not found.");

                var target = await _characterService.GetByIdAsync(toId)
                    ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Target character not found.");

                await EnsureCharacterOwnershipAsync(source);

                source.Currencies ??= new List<Currency>();
                target.Currencies ??= new List<Currency>();

                // Balance check
                foreach (var currency in currencies)
                {
                    var existing = source.Currencies.FirstOrDefault(c => c.Type == currency.Type);

                    if (existing == null)
                        throw CustomExceptions.ThrowNotFoundException(
                            _logger,
                            $"Source character does not have currency type '{currency.Type}'.");

                    if (existing.Amount < currency.Amount)
                        throw CustomExceptions.ThrowInvalidOperationException(
                            _logger,
                            $"Not enough {currency.Type} to transfer {currency.Amount}. Available: {existing.Amount}.");
                }

                foreach (var currency in currencies)
                    UpdateCurrencyList(source.Currencies, currency, isAddition: false);

                source.Currencies.RemoveAll(c => c.Amount <= 0);
                target.Currencies = MergeCurrencies(target.Currencies, currencies);

                await _characterService.UpdateAsync(source);
                await _characterService.UpdateAsync(target);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error transferring currencies from {FromId} to {ToId}", fromId, toId);
                throw;
            }
        }


        // ----------------------------
        // PRIVATE HELPERS
        // ----------------------------

        private async Task EnsureCharacterOwnershipAsync(Character character)
        {
            var userId = _user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                throw CustomExceptions.ThrowUnauthorizedAccessException(_logger, "User not authenticated.");

            var isOwner = character.OwnerIds?.Any(o => o == userId) ?? false;
            var isAdmin = _user.IsInRole("Admin");

            if (!isOwner && !isAdmin)
                throw CustomExceptions.ThrowUnauthorizedAccessException(_logger, "You do not have access to this character.");
        }

        private void UpdateCurrencyList(List<Currency> currencies, Currency currency, bool isAddition)
        {
            var existing = currencies.FirstOrDefault(x => x.Type == currency.Type);

            if (existing == null)
            {
                if (isAddition)
                {
                    currencies.Add(new Currency
                    {
                        Type = currency.Type,
                        Amount = currency.Amount,
                        CurrencyCode = currency.CurrencyCode
                    });
                }
                else
                {
                    throw CustomExceptions.ThrowNotFoundException(_logger,
                        $"Currency type '{currency.Type}' not found.");
                }
            }
            else
            {
                if (isAddition)
                {
                    existing.Amount += currency.Amount;
                }
                else
                {
                    if (existing.Amount < currency.Amount)
                        throw CustomExceptions.ThrowInvalidOperationException(_logger,
                            $"Not enough {currency.Type} to remove {currency.Amount}.");

                    existing.Amount -= currency.Amount;
                }
            }
        }

        private static List<Currency> MergeCurrencies(IEnumerable<Currency> existing, IEnumerable<Currency> incoming)
        {
            return existing
                .Concat(incoming)
                .GroupBy(c => c.CurrencyCode)
                .Select(g => new Currency
                {
                    Type = g.First().Type,
                    CurrencyCode = g.First().CurrencyCode,
                    Amount = g.Sum(c => c.Amount)
                })
                .Where(c => c.Amount > 0)
                .ToList();
        }
    }
}
