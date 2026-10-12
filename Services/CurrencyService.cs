using dndhelper.Authentication.Interfaces;
using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using dndhelper.Services.CharacterServices.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
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
        private readonly IEntitySyncService _entitySyncService;
        private readonly IAuthService _authService;
        private readonly ICampaignService _campaignService;
        private readonly ILogger _logger;

        public CurrencyService(
            ICharacterService characterService,
            IInventoryService inventoryService,
            IInternalBaseService<Character> characterInternalService,
            IInternalBaseService<Inventory> inventoryInternalService,
            IEntitySyncService entitySyncService,
            IAuthService authService,
            ICampaignService campaignService,
            ILogger logger)
        {
            _characterService = Guard.NotNull(characterService, nameof(characterService));
            _inventoryService = Guard.NotNull(inventoryService, nameof(inventoryService));
            _characterInternalService = Guard.NotNull(characterInternalService, nameof(characterInternalService));
            _inventoryInternalService = Guard.NotNull(inventoryInternalService, nameof(inventoryInternalService));
            _entitySyncService = Guard.NotNull(entitySyncService, nameof(entitySyncService));
            _authService = Guard.NotNull(authService, nameof(authService));
            _campaignService = Guard.NotNull(campaignService, nameof(campaignService));
            _logger = Guard.NotNull(logger, nameof(logger));
        }

        #region Character Methods

        public async Task<List<Currency>> GetCharacterCurrenciesAsync(string characterId)
        {
            Guard.NotNullOrWhiteSpace(characterId, nameof(characterId));

            var character = await _characterService.GetByIdAsync(characterId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Character not found.");

            return character.Currencies ?? new List<Currency>();
        }

        public async Task RemoveCurrencyFromCharacter(string characterId, List<Currency> currencies)
        {
            Guard.NotNullOrWhiteSpace(characterId, nameof(characterId));
            var coins = Normalize(currencies);

            var character = await _characterService.GetByIdAsync(characterId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Character not found.");

            Subtract(character.Currencies ??= new List<Currency>(), coins, character.Name ?? "Character");
            await _characterService.UpdateAsync(character);

            _logger.Information("Removed {Currencies} from character {CharacterId}", Summary(coins), characterId);
        }

        public async Task RemoveCurrencyFromCharacterAndNotifyAsync(string characterId, List<Currency> currencies)
        {
            await RemoveCurrencyFromCharacter(characterId, currencies);

            var character = await _characterService.GetByIdAsync(characterId);
            if (character != null)
                await BroadcastCharacterChangeAsync(character, "updated", character);
        }

        public async Task TransferManyToCharacter(string targetId, List<Currency> currencies)
        {
            Guard.NotNullOrWhiteSpace(targetId, nameof(targetId));
            var coins = Normalize(currencies);

            var character = await _characterService.GetByIdAsync(targetId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Target character not found.");

            Add(character.Currencies ??= new List<Currency>(), coins);
            await _characterService.UpdateAsync(character);

            _logger.Information("Added {Currencies} to character {CharacterId}", Summary(coins), targetId);
        }

        public async Task TransferManyToCharacterAndNotifyAsync(string targetId, List<Currency> currencies)
        {
            await TransferManyToCharacter(targetId, currencies);

            var target = await _characterService.GetByIdAsync(targetId);
            if (target != null)
                await BroadcastCharacterChangeAsync(target, "updated", target);
        }

        public async Task TransferBetweenCharacters(string fromId, string toId, List<Currency> currencies)
        {
            Guard.NotNullOrWhiteSpace(fromId, nameof(fromId));
            Guard.NotNullOrWhiteSpace(toId, nameof(toId));
            Guard.That(fromId != toId, "Cannot transfer to the same character.", nameof(toId));
            var coins = Normalize(currencies);

            var source = await _characterService.GetByIdAsync(fromId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Source character not found.");

            var target = await _characterInternalService.GetByIdInternalAsync(toId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Target character not found.");

            Subtract(source.Currencies ??= new List<Currency>(), coins, source.Name ?? "Source character");
            Add(target.Currencies ??= new List<Currency>(), coins);

            await _characterService.UpdateAsync(source);
            await _characterInternalService.UpdateInternalAsync(target);

            _logger.Information("Transferred {Currencies} from {FromId} to {ToId}", Summary(coins), fromId, toId);
        }

        public async Task TransferBetweenCharactersAndNotifyAsync(string fromId, string toId, List<Currency> currencies)
        {
            await TransferBetweenCharacters(fromId, toId, currencies);

            var fromCharacter = await _characterService.GetByIdAsync(fromId);
            var toCharacter = await _characterService.GetByIdInternalAsync(toId);

            if (fromCharacter != null)
                await BroadcastCharacterChangeAsync(fromCharacter, "updated", fromCharacter);

            if (toCharacter != null)
                await BroadcastCharacterChangeAsync(toCharacter, "updated", toCharacter);
        }

        #endregion

        #region Inventory Methods

        public async Task AddCurrenciesToInventory(string inventoryId, List<Currency> currencies)
        {
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
            var coins = Normalize(currencies);

            var inventory = await _inventoryService.GetByIdAsync(inventoryId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Inventory not found.");

            Add(inventory.Currencies ??= new List<Currency>(), coins);
            await _inventoryService.UpdateAsync(inventory);

            _logger.Information("Added {Currencies} to inventory {InventoryId}", Summary(coins), inventoryId);
        }

        public async Task AddCurrenciesToInventoryAndNotifyAsync(string inventoryId, List<Currency> currencies)
        {
            await AddCurrenciesToInventory(inventoryId, currencies);
            await BroadcastInventoryAsync(inventoryId);
        }

        public async Task RemoveCurrenciesFromInventory(string inventoryId, List<Currency> currencies)
        {
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
            var coins = Normalize(currencies);

            var inventory = await _inventoryService.GetByIdAsync(inventoryId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Inventory not found.");

            Subtract(inventory.Currencies ??= new List<Currency>(), coins, inventory.Name);
            await _inventoryService.UpdateAsync(inventory);

            _logger.Information("Removed {Currencies} from inventory {InventoryId}", Summary(coins), inventoryId);
        }

        public async Task RemoveCurrenciesFromInventoryAndNotifyAsync(string inventoryId, List<Currency> currencies)
        {
            await RemoveCurrenciesFromInventory(inventoryId, currencies);
            await BroadcastInventoryAsync(inventoryId);
        }

        public async Task ClaimFromInventory(string characterId, string inventoryId, List<Currency> currencies)
        {
            Guard.NotNullOrWhiteSpace(characterId, nameof(characterId));
            Guard.NotNullOrWhiteSpace(inventoryId, nameof(inventoryId));
            var coins = Normalize(currencies);

            var character = await _characterService.GetByIdAsync(characterId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Character not found.");

            var inventory = await _inventoryInternalService.GetByIdInternalAsync(inventoryId)
                ?? throw CustomExceptions.ThrowNotFoundException(_logger, "Inventory not found.");

            Subtract(inventory.Currencies ??= new List<Currency>(), coins, inventory.Name);
            Add(character.Currencies ??= new List<Currency>(), coins);

            await _inventoryInternalService.UpdateInternalAsync(inventory);
            await _characterService.UpdateAsync(character);

            _logger.Information("{CharacterId} claimed {Currencies} from inventory {InventoryId}",
                characterId, Summary(coins), inventoryId);
        }

        public async Task ClaimFromInventoryAndNotifyAsync(string characterId, string inventoryId, List<Currency> currencies)
        {
            await ClaimFromInventory(characterId, inventoryId, currencies);

            var character = await _characterService.GetByIdAsync(characterId);
            if (character != null)
                await BroadcastCharacterChangeAsync(character, "updated", character);

            await BroadcastInventoryAsync(inventoryId);
        }

        private async Task BroadcastInventoryAsync(string inventoryId)
        {
            var inventory = await _inventoryService.GetByIdAsync(inventoryId);
            if (inventory != null)
                await BroadcastInventoryChangeAsync(inventory, "updated", inventory);
        }

        #endregion

        #region Notifications
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
        #endregion

        #region Mutation

        private static bool IsType(Currency c, string type) =>
            string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Validates a request's coins (trust boundary): trims types, rejects blank types and
        /// non-positive amounts, folds case-variant duplicates ("GP" + "gp") into one entry.
        /// </summary>
        private static List<Currency> Normalize(IEnumerable<Currency>? currencies)
        {
            var result = new List<Currency>();
            foreach (var c in currencies ?? Enumerable.Empty<Currency>())
            {
                var type = c?.Type?.Trim();
                if (string.IsNullOrEmpty(type))
                    throw new ArgumentException("Currency type is required.");
                if (type.Length > 32)
                    throw new ArgumentException("Currency type must be 32 characters or fewer.");
                if (c!.Amount <= 0)
                    throw new ArgumentException($"Amount of {type} must be greater than zero.");

                var existing = result.FirstOrDefault(x => IsType(x, type));
                if (existing != null) existing.Amount = checked(existing.Amount + c.Amount);
                else result.Add(new Currency { Type = type, Amount = c.Amount });
            }

            if (result.Count == 0)
                throw new ArgumentException("No currencies provided.");
            return result;
        }

        private static void Add(List<Currency> wallet, IEnumerable<Currency> coins)
        {
            foreach (var coin in coins)
            {
                var existing = wallet.FirstOrDefault(c => IsType(c, coin.Type));
                if (existing != null) existing.Amount = checked(existing.Amount + coin.Amount);
                else wallet.Add(new Currency { Type = coin.Type, Amount = coin.Amount });
            }
        }

        /// <summary>Checks every coin first so a short balance never leaves a half-applied removal.</summary>
        private static void Subtract(List<Currency> wallet, IReadOnlyCollection<Currency> coins, string owner)
        {
            foreach (var coin in coins)
            {
                var has = wallet.FirstOrDefault(c => IsType(c, coin.Type))?.Amount ?? 0;
                if (has < coin.Amount)
                    throw new InvalidOperationException($"{owner} has only {has} {coin.Type}, needs {coin.Amount}.");
            }

            foreach (var coin in coins)
                wallet.First(c => IsType(c, coin.Type)).Amount -= coin.Amount;

            wallet.RemoveAll(c => c.Amount == 0);
        }

        private static string Summary(IEnumerable<Currency> coins) =>
            string.Join(", ", coins.Select(c => $"{c.Amount} {c.Type}"));

        #endregion

        #region Shop wallet (gp/sp only, 1 gp = 100 sp)

        public void ValidateCharacterHasFunds(Character character, int costInSp)
        {
            int totalWalletSp = GetTotalSilverValue(character.Currencies);
            if (totalWalletSp < costInSp)
            {
                throw CustomExceptions.ThrowInvalidOperationException(
                    _logger,
                    $"Insufficient funds. This transaction costs {costInSp / 100.0} gp, but you only have {totalWalletSp / 100.0} gp value.");
            }
        }

        public void DeductAndConsolidateCurrency(Character character, int costInSp)
        {
            int totalWalletSp = GetTotalSilverValue(character.Currencies);
            if (totalWalletSp < costInSp)
            {
                throw new InvalidOperationException("Insufficient funds.");
            }

            character.Currencies ??= new List<Currency>();

            int currentGp = character.Currencies.Where(c => IsType(c, "gp")).Sum(c => c.Amount);
            int currentSp = character.Currencies.Where(c => IsType(c, "sp")).Sum(c => c.Amount);

            int costGp = costInSp / 100;
            int costSp = costInSp % 100;

            // Step 1: Handle silver deficit by converting gold to silver if needed (gp -> sp change)
            if (currentSp < costSp)
            {
                int deficitSp = costSp - currentSp;
                int gpToConvert = (int)Math.Ceiling(deficitSp / 100.0);

                currentGp -= gpToConvert;
                currentSp += gpToConvert * 100;
            }

            // Step 2: Deduct silver cost
            currentSp -= costSp;

            // Step 3: Handle gold deficit by paying with silver if needed (sp -> gp coverage)
            if (currentGp < costGp)
            {
                int deficitGp = costGp - currentGp;
                currentSp -= deficitGp * 100;
                currentGp += deficitGp;
            }

            // Step 4: Deduct gold cost
            currentGp -= costGp;

            SetGoldAndSilver(character.Currencies, currentGp, currentSp);
        }

        public void AddAndConsolidateCurrency(Character character, int amountInSp)
        {
            int totalWalletSp = GetTotalSilverValue(character.Currencies);
            ConsolidateWallet(character.Currencies ??= new List<Currency>(), totalWalletSp + amountInSp);
        }

        public void AddCurrencyToInventory(Inventory inventory, int amountInSp)
        {
            int totalSp = GetTotalSilverValue(inventory.Currencies);
            ConsolidateWallet(inventory.Currencies ??= new List<Currency>(), totalSp + amountInSp);
        }

        public void RemoveCurrencyFromInventory(Inventory inventory, int amountInSp, bool allowNegative = true)
        {
            int totalSp = GetTotalSilverValue(inventory.Currencies);
            if (!allowNegative && totalSp < amountInSp)
            {
                throw new InvalidOperationException("Insufficient inventory funds.");
            }
            ConsolidateWallet(inventory.Currencies ??= new List<Currency>(), totalSp - amountInSp);
        }

        private static int GetTotalSilverValue(List<Currency>? currencies)
        {
            if (currencies == null) return 0;
            int gp = currencies.Where(c => IsType(c, "gp")).Sum(c => c.Amount);
            int sp = currencies.Where(c => IsType(c, "sp")).Sum(c => c.Amount);
            return (gp * 100) + sp;
        }

        private static void ConsolidateWallet(List<Currency> currencies, int totalSp) =>
            SetGoldAndSilver(currencies, totalSp / 100, totalSp % 100);

        /// <summary>Rewrites only the gp/sp entries; every other coin (pp, cp, custom) is left untouched.</summary>
        private static void SetGoldAndSilver(List<Currency> currencies, int gp, int sp)
        {
            currencies.RemoveAll(c => IsType(c, "gp") || IsType(c, "sp"));
            if (gp != 0) currencies.Add(new Currency { Type = "gp", Amount = gp });
            if (sp != 0) currencies.Add(new Currency { Type = "sp", Amount = sp });
        }

        #endregion
    }
}
