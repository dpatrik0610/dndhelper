using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class CharacterService : ICharacterService
    {
        private readonly ICharacterRepository _repository;

        public CharacterService(ICharacterRepository repository)
        {
            _repository = repository;
        }

        public Task<IEnumerable<Character>> GetAllAsync() =>
            _repository.GetAllAsync();
        public async Task<IEnumerable<Character>> GetByIdsAsync(IEnumerable<string> ids) =>
            await _repository.GetByIds(ids);

        public Task<Character?> GetByIdAsync(string id) =>
            _repository.GetByIdAsync(id);

        public Task<Character> CreateAsync(Character character) =>
            _repository.AddAsync(character);

        public Task<Character?> UpdateAsync(Character character) =>
            _repository.UpdateAsync(character);

        public Task<bool> DeleteAsync(string id) =>
            _repository.DeleteAsync(id);
    }
}
