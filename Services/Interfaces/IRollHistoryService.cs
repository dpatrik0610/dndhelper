using dndhelper.Models.RollModels;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IRollHistoryService
    {
        Task<RollRecord?> CreateAsync(RollRecord record);
    }
}
