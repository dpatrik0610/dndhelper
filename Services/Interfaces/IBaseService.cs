using dndhelper.Authorization;
using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IBaseService<T> where T : class, IEntity
    {
        Task<T?> CreateAsync(T entity);
        Task<List<T>> CreateManyAsync(List<T> entities);

        Task<T?> GetByIdAsync(string id);
        Task<List<T>> GetByIdsAsync(IEnumerable<string> ids);
        Task<IEnumerable<T>> GetAllAsync();
        Task<long> CountAsync();
        Task<bool> ExistsAsync(string id);

        Task<T?> UpdateAsync(T entity);

        Task<bool> DeleteAsync(string id);
        Task<bool> LogicDeleteAsync(string id);
    }
}
