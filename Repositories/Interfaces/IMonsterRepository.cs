using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IMonsterRepository : IRepository<Monster>
    {
        Task<List<Monster>> FindByNamePhraseAsync(string namePhrase);
        Task<List<Monster>> GetPagedAsync(int page, int pageSize);
        Task<List<Monster>> SearchAsync(string query, int page, int pageSize);
        Task<long> GetCountAsync();

        Task<List<Monster>> SearchAsync(MonsterSearchCriteria criteria);
        Task<List<Monster>> FindByOwnerIdAsync(string ownerId);
    }
}