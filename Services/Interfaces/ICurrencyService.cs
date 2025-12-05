using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ICurrencyService
    {
        Task<List<Currency>> GetCharacterCurrenciesAsync(string characterId);
        Task TransferManyToCharacter(string targetId, List<Currency> currencies);
        Task RemoveCurrencyFromCharacter(string characterId, List<Currency> currencies);
        Task AddCurrencyToInventory(string inventoryId, Currency currency);
        Task TransferBetweenCharacters(string fromId, string toId, List<Currency> currencies);
        Task ClaimFromInventory(string characterId, string inventoryId, List<Currency> currencies);

        // Orchestration helpers (include notifications)
        Task RemoveCurrencyFromCharacterAndNotifyAsync(string characterId, List<Currency> currencies);
        Task TransferManyToCharacterAndNotifyAsync(string targetId, List<Currency> currencies);
        Task AddCurrenciesToInventoryAndNotifyAsync(string inventoryId, List<Currency> currencies);
        Task TransferBetweenCharactersAndNotifyAsync(string fromId, string toId, List<Currency> currencies);
        Task ClaimFromInventoryAndNotifyAsync(string characterId, string inventoryId, List<Currency> currencies);
    }
}
