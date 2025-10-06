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
        Task<InventoryItem?> GetItemAsync(string inventoryId, string equipmentId);
        Task<InventoryItem> AddOrIncrementItemAsync(string inventoryId, InventoryItem item);
        Task<InventoryItem> AddNewItemAsync(string inventoryId, Equipment equipment);
        Task UpdateItemAsync(string inventoryId, InventoryItem item);
        Task DeleteItemAsync(string inventoryId, string equipmentId);
        Task<bool> ItemExistsInInventoryAsync(string inventoryId, string equipmentId);
        Task DecrementItemQuantityAsync(string inventoryId, string equipmentId, int decrementBy = 1);
    }
}
