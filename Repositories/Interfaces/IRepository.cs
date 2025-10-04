using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IRepository<T> where T : IEntity
    {
        Task<T?> GetByIdAsync(string id);
        Task<IEnumerable<T>> GetAllAsync();
        Task<T> CreateAsync(T entity);
        Task<List<T>> CreateManyAsync(List<T> entities);
        Task<T?> UpdateAsync(T entity);
        Task<bool> DeleteAsync(string id);
        Task<bool> LogicDeleteAsync(string id);
        Task<bool> ExistsAsync(string id);
        Task<long> CountAsync();

        // Cache management
        void AddToCache(T entity);
        T? GetFromCache(string id);
        void UpdateCache(T entity);
        void RemoveFromCache(string id);
    }
}
