using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IEquipmentRepository : IRepository<Equipment>
    {
        Task<Equipment?> GetByIndexAsync(string index);
        Task DeleteByIndex(string index);
        Task<List<Equipment>> GetByIdsAsync(IEnumerable<string> ids);
        Task<PagedResult<Equipment>> GetAllPaginatedAsync(int page, int pageSize);
    }
}
