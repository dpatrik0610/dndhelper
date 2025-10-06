using dndhelper.Models.CharacterModels;
using dndhelper.Services.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.CharacterServices.Interfaces
{
    public interface ICharacterService : IBaseService<Character>
    {
        Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId);
    }
}
