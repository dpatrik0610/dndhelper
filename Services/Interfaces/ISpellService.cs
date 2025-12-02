using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ISpellService: IBaseService<Spell>, IInternalBaseService<Spell>
    {
        Task<List<SpellNameResponse>> GetAllNamesAsync();
    }
}
