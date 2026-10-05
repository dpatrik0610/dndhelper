using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IMonsterRepository : IRepository<Monster>
    {
        Task<List<Monster>> FindByNamePhraseAsync(string namePhrase, FilterDefinition<Monster> scope);
        Task<List<Monster>> GetPagedAsync(FilterDefinition<Monster> scope, int page, int pageSize);
        Task<List<Monster>> SearchAsync(FilterDefinition<Monster> scope, string query, int page, int pageSize);
        Task<long> GetCountAsync(FilterDefinition<Monster> scope);

        Task<List<Monster>> SearchAsync(FilterDefinition<Monster> scope, MonsterSearchCriteria criteria);
        Task<List<Monster>> FindByOwnerIdAsync(FilterDefinition<Monster> scope, string ownerId);
    }
}