using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IPublicDndApiClient
    {
        Task<IEnumerable<Equipment>> GetEquipmentListAsync();
        Task<Equipment?> GetEquipmentByIndexAsync(string index);
    }
}
