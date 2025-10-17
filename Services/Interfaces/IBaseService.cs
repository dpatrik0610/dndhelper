using dndhelper.Authorization;
using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IBaseService<T> where T : class, IEntity
    {
        // CREATE
        Task<T?> CreateAsync(T entity);
        Task<List<T>> CreateManyAsync(List<T> entities);

        // READ
        Task<T?> GetByIdAsync(string id);
        Task<List<T>> GetByIdsAsync(IEnumerable<string> ids);
        Task<IEnumerable<T>> GetAllAsync();
        Task<long> CountAsync();
        Task<bool> ExistsAsync(string id);

        // UPDATE
        Task<T?> UpdateAsync(T entity);

        // DELETE
        Task<bool> DeleteAsync(string id);
        Task<bool> LogicDeleteAsync(string id);
    }
}
