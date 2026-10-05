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
        /// <summary>Atomically adds a character to the user's list (no-op if already there).</summary>
        Task AddCharacterIdAsync(string userId, string characterId);
        Task RemoveCharacterIdAsync(string userId, string characterId);
        Task UpdateCampaignIds(User user, List<string> campaignIds);
        Task RefreshLastLogin(string username);
        Task LogicDeleteAsync(User user);
    }
}
