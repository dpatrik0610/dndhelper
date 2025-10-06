using dndhelper.Authentication;
using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IInventoryService : IBaseService<Inventory>
    {
        // Inventories
        Task<IEnumerable<Inventory>> GetByCharacterAsync(string characterId);

        // Inventory Items
        Task<IEnumerable<InventoryItem>> GetItemsAsync(string inventoryId);
        Task<InventoryItem?> GetItemAsync(string inventoryId, string equipmentIndex);
        Task AddItemAsync(string inventoryId, InventoryItem item);
        Task UpdateItemAsync(string inventoryId, InventoryItem item);
        Task DeleteItemAsync(string inventoryId, string equipmentIndex);
    }
}
