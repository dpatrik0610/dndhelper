using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IMonsterService
    {
        Task<Monster> CreateMonsterAsync(Monster monster);
        Task<Monster?> GetMonsterByIdAsync(string id);
        Task<List<Monster>> GetMonstersByNameAsync(string name);
        Task<List<Monster>> GetAllMonstersAsync();
        Task<List<Monster>> GetPagedMonstersAsync(int page, int pageSize);
        Task<List<Monster>> SearchMonstersAsync(string query, int page, int pageSize);
        Task<bool> MonsterExistsAsync(string id);
        Task<Monster> UpdateMonsterAsync(Monster monster);
        Task DeleteMonsterAsync(string id);
        Task<bool> LogicDeleteMonsterAsync(string id);

        // Ownership and Management
        Task<bool> AddMonsterOwnerAsync(string monsterId, string newOwnerId, string requesterUserId);
        Task<bool> SwitchMonsterOwnerAsync(string monsterId, string newOwnerId, string requesterUserId);
        Task<List<Monster>> GetMonstersByOwnerAsync(string ownerId);
        Task<bool> DeleteOwnMonsterAsync(string monsterId, string userId);

        // Advanced Search
        Task<List<Monster>> AdvancedSearchAsync(MonsterSearchCriteria criteria);
    }
}
