using dndhelper.Authentication;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories.Interfaces
{
    public interface IUserRepository
    {
        Task<User> GetByUsernameAsync(string username);
        Task<User> GetByIdAsync(string id);
        Task<List<User>> GetAllAsync();
        Task CreateAsync(User user);
        Task<bool> CheckUserExists(string username);
        Task UpdateAsync(User user);
        Task LogicDeleteAsync(User user);
        Task UpdateCharacterIds(User user, List<string> characterIds);
        Task UpdateCampaignIds(User user, List<string> campaignIds);
        Task RefreshLastLogin(string username);
        Task DeleteAsync(string id);
    }
}
