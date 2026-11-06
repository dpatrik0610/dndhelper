using dndhelper.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IChronicleCommentRepository : IRepository<ChronicleComment>
    {
        Task<List<ChronicleComment>> GetByEntryIdAsync(string entryId);

        Task DeleteByEntryIdAsync(string entryId);
    }
}
