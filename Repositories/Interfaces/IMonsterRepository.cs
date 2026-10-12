using dndhelper.Models;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IMonsterRepository : IRepository<Monster>
    {
        Task<(List<Monster> Items, long Total)> SearchAsync(FilterDefinition<Monster> scope, MonsterSearchCriteria criteria);
        Task<List<string>> GetTypesAsync(FilterDefinition<Monster> scope);
        /// <summary>Every live monster with one of these names (case-insensitive), in any campaign.</summary>
        Task<List<Monster>> FindByNamesAsync(IEnumerable<string> names);
    }
}
