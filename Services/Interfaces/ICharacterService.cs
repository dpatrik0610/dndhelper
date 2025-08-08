using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ICharacterService
    {
        Task<IEnumerable<Character>> GetAllAsync();
        Task<IEnumerable<Character>> GetByIdsAsync(IEnumerable<string> ids);
        Task<Character?> GetByIdAsync(string id);
        Task<Character> CreateAsync(Character character);
        Task<Character?> UpdateAsync(Character character);
        Task<bool> DeleteAsync(string id);
    }
}
