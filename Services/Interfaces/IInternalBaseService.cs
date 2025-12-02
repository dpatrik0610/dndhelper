using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IInternalBaseService<T> where T : class, IEntity
    {
        Task<T?> GetByIdInternalAsync(string id);
        Task<List<T>> GetByIdsInternalAsync(IEnumerable<string> ids);
        Task<IEnumerable<T>> GetAllInternalAsync();
        Task<T?> UpdateInternalAsync(T entity);
        Task<long> CountInternalAsync();
        Task<bool> ExistsInternalAsync(string id);
    }
}
