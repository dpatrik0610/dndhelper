using dndhelper.Models;
using dndhelper.Models.DTOs;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ILootService
    {
        /// <summary>Rolls loot and stores it in a new inventory named by the request.</summary>
        Task<Inventory> GenerateAsync(LootRequest request);
    }
}
