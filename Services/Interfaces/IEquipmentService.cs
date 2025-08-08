using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IEquipmentService
    {
        Task<IEnumerable<Equipment>> GetAllEquipmentAsync(); // combines local + public API
        Task<Equipment?> GetEquipmentByIndexAsync(string index);
        Task<Equipment> CreateEquipmentAsync(Equipment equipment);
        Task<Equipment> UpdateEquipmentAsync(Equipment equipment);
        Task DeleteEquipmentAsync(string index);
        Task<bool> CheckIfIndexExists(string index);
    }
}
