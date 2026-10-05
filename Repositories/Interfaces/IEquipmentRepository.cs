using dndhelper.Models;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IEquipmentRepository : IRepository<Equipment>
    {
        Task<Equipment?> GetByIndexAsync(string index, FilterDefinition<Equipment> scope);
        Task DeleteByIndex(string index);
        Task<List<Equipment>> GetByIdsAsync(IEnumerable<string> ids);
        Task<PagedResult<Equipment>> GetAllPaginatedAsync(FilterDefinition<Equipment> scope, int page, int pageSize, string? tag = null, string? tier = null, string? damageType = null, string? name = null);
    }
}
