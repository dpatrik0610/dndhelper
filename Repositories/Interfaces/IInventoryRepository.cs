using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IInventoryRepository : IRepository<Inventory>
    {
        Task<IEnumerable<Inventory>> GetByCharacterIdAsync(string characterId);

        Task<IEnumerable<InventoryItem>> GetItemsAsync(string inventoryId);
        Task<InventoryItem?> GetItemAsync(string inventoryId, string equipmentIndex);
        Task<InventoryItem?> AddItemAsync(string inventoryId, InventoryItem item);
        Task UpdateItemAsync(string inventoryId, InventoryItem item);
        Task DeleteItemAsync(string inventoryId, string equipmentIndex);
    }
}
