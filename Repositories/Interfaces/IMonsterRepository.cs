using System.Collections.Generic;
using System.Threading.Tasks;
using dndhelper.Authentication;
using dndhelper.Models;

namespace dndhelper.Repositories.Interfaces
{
    public interface IMonsterRepository
    {
        Task<Monster?> GetByIdAsync(string id);
        Task<List<Monster>> FindByNamePhraseAsync(string namePhrase);
        Task<List<Monster>> GetAllAsync();
        Task<List<Monster>> GetPagedAsync(int page, int pageSize);
        Task<bool> ExistsAsync(string id);
        Task CreateAsync(Monster monster);
        Task UpdateAsync(Monster monster);
        Task DeleteAsync(string id);
        Task<bool> LogicDeleteAsync(string id);
        Task<List<Monster>> SearchAsync(string query, int page, int pageSize);

        Task<List<Monster>> SearchAsync(MonsterSearchCriteria criteria);
        Task<List<Monster>> FindByOwnerIdAsync(string ownerId);
    }
}