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
        Task AddCurrencyToInventory(string inventoryId, Currency currency);
    }
}
