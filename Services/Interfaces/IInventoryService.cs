using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IInventoryService
    {
        // Inventories
        Task<IEnumerable<Inventory>> GetInventoriesByCharacterAsync(string characterId);
        Task<Inventory?> GetInventoryByIdAsync(string id);
        Task<Inventory> CreateInventoryAsync(Inventory inventory);
        Task<Inventory> UpdateInventoryAsync(Inventory inventory);
        Task DeleteInventoryAsync(string id);

        // Inventory Items
        Task<IEnumerable<InventoryItem>> GetItemsAsync(string inventoryId);
        Task<InventoryItem?> GetItemAsync(string inventoryId, string equipmentIndex);
        Task AddItemAsync(string inventoryId, InventoryItem item);
        Task UpdateItemAsync(string inventoryId, InventoryItem item);
        Task DeleteItemAsync(string inventoryId, string equipmentIndex);
    }
}
