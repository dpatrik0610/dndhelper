using dndhelper.Models;
using System.IO;
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

        Task<(Stream Content, string ContentType)?> OpenImageAsync(string id);
        Task DeleteImageAsync(string id);
    }
}
