using dndhelper.Authentication;
using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IMonsterService : IBaseService<Monster>
    {
        Task<List<Monster>> GetMonstersByNameAsync(string name);
        Task<List<Monster>> GetPagedMonstersAsync(int page, int pageSize);
        Task<List<Monster>> SearchMonstersAsync(string query, int page, int pageSize);
        Task<long> GetCountAsync();

        // Ownership and Management
        Task<bool> AddMonsterOwnerAsync(string monsterId, string newOwnerId, string requesterUserId);
        Task<bool> SwitchMonsterOwnerAsync(string monsterId, string newOwnerId, string requesterUserId);
        Task<List<Monster>> GetMonstersByOwnerAsync(string ownerId);
        Task<bool> DeleteOwnMonsterAsync(string monsterId, string userId);

        // Advanced Search
        Task<List<Monster>> AdvancedSearchAsync(MonsterSearchCriteria criteria);
    }
}
