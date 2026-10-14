using dndhelper.Models;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface ITabletopRepository
    {
        Task<Tabletop?> GetByIdAsync(string id);
        Task<Tabletop?> GetByJoinCodeAsync(string joinCode);
        Task<Tabletop?> GetByCampaignIdAsync(string campaignId);
        Task<Tabletop> GetOrCreateForCampaignAsync(string campaignId);
        Task ReplaceAsync(Tabletop table);
        Task<bool> AnyUsesMapAsync(string imageUrl);
        Task<bool> AnyUsesTokenImageAsync(string imageUrl);
    }
}
