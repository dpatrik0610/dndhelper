using System.Collections.Generic;
using System.Threading.Tasks;
using dndhelper.Authentication;
using dndhelper.Models;

namespace dndhelper.Repositories.Interfaces
{
    public interface IMonsterRepository
    {
        Task<List<Monster>> FindByNamePhraseAsync(string namePhrase);
        Task<List<Monster>> GetPagedAsync(int page, int pageSize);
        Task<List<Monster>> SearchAsync(string query, int page, int pageSize);

        Task<List<Monster>> SearchAsync(MonsterSearchCriteria criteria);
        Task<List<Monster>> FindByOwnerIdAsync(string ownerId);
    }
}