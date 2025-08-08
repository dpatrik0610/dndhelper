using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface ICharacterRepository
    {
        Task<Character?> GetByIdAsync(string id);
        Task<IEnumerable<Character>> GetByIds(IEnumerable<string> ids);
        Task<IEnumerable<Character>> GetAllAsync();
        Task<Character> AddAsync(Character character);
        Task<Character?> UpdateAsync(Character character);
        Task<bool> DeleteAsync(string id);
    }
}
