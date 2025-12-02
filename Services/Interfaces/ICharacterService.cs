using dndhelper.Models.CharacterModels;
using dndhelper.Services.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.CharacterServices.Interfaces
{
    public interface ICharacterService : IBaseService<Character>, IInternalBaseService<Character>
    {
        Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId);
        Task<bool> UseSpellSlotAsync(string characterId, int level);
        Task<bool> RecoverSpellSlotAsync(string characterId, int level, int amount = 1);
        Task<bool> LongRestAsync(string characterId);
    }
}
