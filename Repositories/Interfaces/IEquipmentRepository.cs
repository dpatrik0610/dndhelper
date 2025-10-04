using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IEquipmentRepository
    {
        Task<Equipment?> GetByIndexAsync(string index);
        Task DeleteByIndex(string index);
    }
}
