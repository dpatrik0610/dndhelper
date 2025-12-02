using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using dndhelper.Services.CharacterServices.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class CurrencyService : ICurrencyService
    {
        private readonly ICharacterService _characterService;
        private readonly IInventoryService _inventoryService;
        private readonly IInternalBaseService<Character> _characterInternalService;
        private readonly IInternalBaseService<Inventory> _inventoryInternalService;
        private readonly ILogger _logger;

        public CurrencyService(
            ICharacterService characterService,
            IInventoryService inventoryService,
            IInternalBaseService<Character> characterInternalService,
            IInternalBaseService<Inventory> inventoryInternalService,
            ILogger logger)
        {
            _characterService = Guard.NotNull(characterService, nameof(characterService));
            _inventoryService = Guard.NotNull(inventoryService, nameof(inventoryService));
            _characterInternalService = Guard.NotNull(characterInternalService, nameof(characterInternalService));
            _inventoryInternalService = Guard.NotNull(inventoryInternalService, nameof(inventoryInternalService));
            _logger = Guard.NotNull(logger, nameof(logger));
        }

        #region Character Methods

        public async Task<List<Currency>> GetCharacterCurrenciesAsync(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
            {
                _logger.Warning("GetCharacterCurrenciesAsync called with empty characterId.");
                return new List<Currency>();
            }

            var character = await _characterService.GetByIdAsync(characterId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Character not found.");

            return character.Currencies ?? new List<Currency>();
        }

        public async Task RemoveCurrencyFromCharacter(string characterId, List<Currency> currencies)
        {
            Guard.NotNullOrWhiteSpace(characterId, nameof(characterId));
            Guard.That(currencies != null && currencies.Count > 0, "No currencies provided.", nameof(currencies));

            try
            {
                var character = await _characterService.GetByIdAsync(characterId);
                if (character?.Currencies == null)
                {
                    _logger.Warning("RemoveCurrencyFromCharacter: character {CharacterId} has no currencies.", characterId);
                    return;
                }

                foreach (var currency in currencies)
                    UpdateCurrencyList(character.Currencies, currency, isAddition: false);

                character.Currencies.RemoveAll(c => c.Amount <= 0);
                await _characterService.UpdateAsync(character);
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Error removing currencies from character {CharacterId}. Requested: {Currencies}",
                    characterId, BuildCurrencySummary(currencies));
                throw;
            }
        }

        public async Task TransferManyToCharacter(string targetId, List<Currency> currencies)
        {
            Guard.NotNullOrWhiteSpace(targetId, nameof(targetId));
            Guard.That(currencies != null && currencies.Count > 0, "No currencies provided.", nameof(currencies));

            try
            {
                var character = await _characterService.GetByIdAsync(targetId)
                    ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Target character not found.");

                character.Currencies ??= new List<Currency>();
                character.Currencies = MergeCurrencies(character.Currencies, currencies);

                _logger.Information(
                    "TransferManyToCharacter: Added currencies to {CharacterId}. {Currencies}",
                    targetId, BuildCurrencySummary(currencies));

                await _characterService.UpdateAsync(character);
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Error transferring currencies to character {CharacterId}. Requested: {Currencies}",
                    targetId, BuildCurrencySummary(currencies));
                throw;
            }
        }

        public async Task TransferBetweenCharacters(string fromId, string toId, List<Currency> currencies)
        {
            Guard.NotNullOrWhiteSpace(fromId, nameof(fromId));
            Guard.NotNullOrWhiteSpace(toId, nameof(toId));
            Guard.That(currencies != null && currencies.Count > 0, "No currencies provided.", nameof(currencies));

            try
            {
                var source = await _characterService.GetByIdAsync(fromId)
                    ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Source character not found.");

                var target = await _characterInternalService.GetByIdInternalAsync(toId)
                    ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Target character not found.");

                source.Currencies ??= new List<Currency>();
                target.Currencies ??= new List<Currency>();

                ValidateSourceHasCurrencies(source, currencies);

                foreach (var currency in currencies)
                    UpdateCurrencyList(source.Currencies, currency, isAddition: false);

                source.Currencies.RemoveAll(c => c.Amount <= 0);
                target.Currencies = MergeCurrencies(target.Currencies, currencies);

                var summary = BuildCurrencySummary(currencies);
                _logger.Information(
                    "Currency transfer between characters: {FromId} -> {ToId} | {Summary}",
                    fromId, toId, summary);

                await _characterService.UpdateAsync(source);
                await _characterInternalService.UpdateInternalAsync(target);
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Error transferring currencies from {FromId} to {ToId}. Requested: {Currencies}",
                    fromId, toId, BuildCurrencySummary(currencies));
                throw;
            }
        }

        #endregion

        #region Inventory Methods
        public async Task AddCurrencyToInventory(string inventoryId, Currency currency)
        {
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));

            try
            {
                var inventory = await _inventoryService.GetByIdAsync(inventoryId)
                    ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Inventory not found.");

                inventory.Currencies ??= new List<Currency>();
                inventory.Currencies = MergeCurrencies(inventory.Currencies, new[] { currency });

                await _inventoryService.UpdateAsync(inventory);
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Error adding currency to inventory {InventoryId}. Currency: {CurrencyType}, Amount: {Amount}",
                    inventoryId, currency?.Type, currency?.Amount);
                throw;
            }
        }

        public async Task ClaimFromInventory(string characterId, string inventoryId, List<Currency> currencies)
        {
            Guard.NotNullOrWhiteSpace(characterId, nameof(characterId));
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
            Guard.That(currencies != null && currencies.Count > 0, "No currencies provided.", nameof(currencies));

            try
            {
                var character = await _characterService.GetByIdAsync(characterId)
                    ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Character not found.");

                var inventory = await _inventoryInternalService.GetByIdInternalAsync(inventoryId)
                    ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Inventory not found.");

                inventory.Currencies ??= new List<Currency>();
                character.Currencies ??= new List<Currency>();

                ValidateInventoryHasCurrencies(inventory, currencies);

                foreach (var currency in currencies)
                    UpdateCurrencyList(inventory.Currencies, currency, isAddition: false);

                inventory.Currencies.RemoveAll(c => c.Amount <= 0);
                character.Currencies = MergeCurrencies(character.Currencies, currencies);

                _logger.Information(
                    "ClaimFromInventory: {CharacterId} claimed from {InventoryId}. {Currencies}",
                    characterId, inventoryId, BuildCurrencySummary(currencies));

                await _inventoryInternalService.UpdateInternalAsync(inventory);
                await _characterService.UpdateAsync(character);
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Error claiming currencies from inventory {InventoryId} to character {CharacterId}. Requested: {Currencies}",
                    inventoryId, characterId, BuildCurrencySummary(currencies));
                throw;
            }
        }

        #endregion

        #region Validation
        private void ValidateSourceHasCurrencies(Character source, IEnumerable<Currency> currencies)
        {
            foreach (var currency in currencies)
            {
                var existing = source.Currencies.FirstOrDefault(c => c.Type == currency.Type);

                if (existing == null)
                    throw CustomExceptions.ThrowNotFoundException(
                        _logger, $"Source missing currency '{currency.Type}'.");

                if (existing.Amount < currency.Amount)
                    throw CustomExceptions.ThrowInvalidOperationException(
                        _logger,
                        $"Not enough {currency.Type}. Needed {currency.Amount}, has {existing.Amount}.");
            }
        }

        private void ValidateInventoryHasCurrencies(Inventory inventory, IEnumerable<Currency> currencies)
        {
            foreach (var currency in currencies)
            {
                var inv = inventory.Currencies.FirstOrDefault(c => c.Type == currency.Type);

                if (inv == null)
                    throw CustomExceptions.ThrowNotFoundException(
                        _logger, $"Inventory missing currency '{currency.Type}'.");

                if (inv.Amount < currency.Amount)
                    throw CustomExceptions.ThrowInvalidOperationException(
                        _logger,
                        $"Inventory has only {inv.Amount} {currency.Type}.");
            }
        }

        #endregion

        #region Mutation
        private void UpdateCurrencyList(List<Currency> list, Currency currency, bool isAddition)
        {
            list = Guard.NotNull(list, nameof(list));
            currency = Guard.NotNull(currency, nameof(currency));

            var existing = list.FirstOrDefault(x => x.Type == currency.Type);

            if (isAddition)
                AddCurrency(list, currency, existing);
            else
                RemoveCurrency(list, currency, existing);
        }

        private void AddCurrency(List<Currency> list, Currency currency, Currency? existing)
        {
            if (existing == null)
            {
                list.Add(new Currency
                {
                    Type = currency.Type,
                    Amount = currency.Amount,
                    CurrencyCode = currency.CurrencyCode
                });
                return;
            }

            existing.Amount += currency.Amount;
        }

        private void RemoveCurrency(List<Currency> list, Currency currency, Currency? existing)
        {
            if (existing == null)
                throw CustomExceptions.ThrowNotFoundException(
                    _logger, $"Currency '{currency.Type}' not found.");

            if (existing.Amount < currency.Amount)
                throw CustomExceptions.ThrowInvalidOperationException(
                    _logger,
                    $"Not enough {currency.Type} to remove {currency.Amount}. Available: {existing.Amount}");

            existing.Amount -= currency.Amount;
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
                    Amount = g.Sum(x => x.Amount)
                })
                .Where(c => c.Amount > 0)
                .ToList();
        }

        private static string BuildCurrencySummary(IEnumerable<Currency> currencies)
        {
            if (currencies == null) return string.Empty;
            return string.Join(", ", currencies.Select(c => $"{c.Amount} {c.Type} ({c.CurrencyCode})"));
        }

        #endregion
    }
}
