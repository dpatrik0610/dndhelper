using dndhelper.Models.RollModels;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface ISubtleRollService
    {
        Task<SubtleRollReceipt> RollSubtleAsync(SubtleRollRequest request);
    }
}
