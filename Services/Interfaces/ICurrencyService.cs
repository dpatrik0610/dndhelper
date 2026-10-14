using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ICurrencyService
    {
        Task<List<Currency>> GetCharacterCurrenciesAsync(string characterId);
        Task TransferManyToCharacter(string targetId, List<Currency> currencies);
        Task RemoveCurrencyFromCharacter(string characterId, List<Currency> currencies);
        Task AddCurrenciesToInventory(string inventoryId, List<Currency> currencies);
        Task RemoveCurrenciesFromInventory(string inventoryId, List<Currency> currencies);
        Task TransferBetweenCharacters(string fromId, string toId, List<Currency> currencies);
        Task ClaimFromInventory(string characterId, string inventoryId, List<Currency> currencies);

        Task RemoveCurrencyFromCharacterAndNotifyAsync(string characterId, List<Currency> currencies);
        Task TransferManyToCharacterAndNotifyAsync(string targetId, List<Currency> currencies);
        Task AddCurrenciesToInventoryAndNotifyAsync(string inventoryId, List<Currency> currencies);
        Task RemoveCurrenciesFromInventoryAndNotifyAsync(string inventoryId, List<Currency> currencies);
        Task TransferBetweenCharactersAndNotifyAsync(string fromId, string toId, List<Currency> currencies);
        Task ClaimFromInventoryAndNotifyAsync(string characterId, string inventoryId, List<Currency> currencies);

        void ValidateCharacterHasFunds(Character character, int costInSp);
        void DeductAndConsolidateCurrency(Character character, int costInSp);
        void AddAndConsolidateCurrency(Character character, int amountInSp);
        void AddCurrencyToInventory(Inventory inventory, int amountInSp);
        void RemoveCurrencyFromInventory(Inventory inventory, int amountInSp, bool allowNegative);
    }
}
