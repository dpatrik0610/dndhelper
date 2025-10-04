using dndhelper.Models.CharacterModels;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.CharacterServices.Interfaces;
using Serilog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services
{

    public class CharacterService : BaseService<Character, ICharacterRepository> , ICharacterService
    {
        public CharacterService(ICharacterRepository repository, ILogger logger)
            : base(repository, logger) { }

        public Task<IEnumerable<Character>> GetByOwnerIdAsync(string ownerId) 
            => _repository.GetByOwnerIdAsync(ownerId);
    }
}
