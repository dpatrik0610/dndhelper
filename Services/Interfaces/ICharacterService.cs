using dndhelper.Models.CharacterModels;
using dndhelper.Services.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.CharacterServices.Interfaces
{
    public interface ICharacterService : IBaseService<Character>, IInternalBaseService<Character>
    {
        Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId);
        Task<List<Character>> GetForUserAsync(string userId, IEnumerable<string>? listedIds);
        Task<bool> UseSpellSlotAsync(string characterId, int level);
        Task<bool> RecoverSpellSlotAsync(string characterId, int level, int amount = 1);
        Task<bool> LongRestAsync(string characterId);
        /// <summary>Uploads token art (images/tokens) and saves its URL on the character. Needs write access to it.</summary>
        Task<Character> UploadImageAsync(string characterId, System.IO.Stream content, string fileName);
        Task<BulkLongRestResult> BulkLongRestAsync(IEnumerable<string> characterIds);
    }
}
