using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ISpellService: IBaseService<Spell>
    {
        Task<List<SpellNameResponse>> GetAllNamesAsync();
    }
}
