using dndhelper.Authentication;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services.Interfaces
{
    public interface IUserService : IBaseService<User>, IInternalBaseService<User>
    {

        // Read
        Task<User> GetSelfAsync(string userId);
        Task<User?> GetByUsernameAsync(string username);
        Task<bool> CheckExistsByUsername(string username);

        // Update
        Task<User?> UpdateEmailAsync(string username, string newEmail);
        Task<User?> UpdateStatusAsync(string username, UserStatus newStatus);
        Task<User?> UpdateCharacterIds(User user, List<string> characterIds);
        Task<User?> UpdateCampaignIds(User user, List<string> campaignIds);
        Task<User?> RefreshLastLogin(string username);
    }
}