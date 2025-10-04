using dndhelper.Authentication;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IUserRepository : IRepository<User>
    {
        Task<User> GetByUsernameAsync(string username);
        Task<bool> CheckUserExists(string username);
        Task UpdateCharacterIds(User user, List<string> characterIds);
        Task UpdateCampaignIds(User user, List<string> campaignIds);
        Task RefreshLastLogin(string username);
        Task LogicDeleteAsync(User user);
    }
}
