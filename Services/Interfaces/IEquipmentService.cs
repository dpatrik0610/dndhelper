using dndhelper.Authentication;
using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IEquipmentService : IBaseService<Equipment>
    {
        Task DeleteByIndexAsync(string index);
        Task<Equipment?> GetEquipmentByIndexAsync(string index);
        Task<bool> CheckIfIndexExists(string index);
        Task<List<Equipment>> SearchByName(string name);
    }
}
