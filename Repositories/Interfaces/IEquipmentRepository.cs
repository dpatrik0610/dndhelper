using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IEquipmentRepository
    {
        Task<IEnumerable<Equipment>> GetEquipmentAsync();
        Task<Equipment?> GetEquipmentByIndexAsync(string index);
        Task<Equipment> AddEquipmentAsync(Equipment equipment);
        Task<bool> AddMultipleEquipmentAsync(IEnumerable<Equipment> equipments);
        Task<Equipment> UpdateEquipmentAsync(Equipment equipment);
        Task DeleteEquipmentAsync(string index);
    }
}
