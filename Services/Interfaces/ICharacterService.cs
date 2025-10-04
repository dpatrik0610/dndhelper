using dndhelper.Models.CharacterModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.CharacterServices.Interfaces
{
    public interface ICharacterService
    {
        Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId);
    }
}
