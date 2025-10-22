using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface ISpellRepository : IRepository<Spell>
    {
        //Task<List<Monster>> SearchAsync(MonsterSearchCriteria criteria);
        Task<List<SpellNameResponse>> GetAllNamesAsync();
    }
}
