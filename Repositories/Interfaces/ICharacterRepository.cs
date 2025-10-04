using dndhelper.Models.CharacterModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface ICharacterRepository
    {
        Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId);
    }
}
