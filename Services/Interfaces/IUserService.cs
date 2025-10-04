using System.Collections.Generic;
using System.Threading.Tasks;
using dndhelper.Authentication;

namespace dndhelper.Services.Interfaces
{
    public interface IUserService
    {
        // Create
        Task<User> CreateAsync(User user);

        // Read
        Task<User> GetSelfAsync(string userId);
        Task<User?> GetByUsernameAsync(string username);
        Task<User?> GetByIdAsync(string id);
        Task<List<User>> GetAllAsync();
        Task<bool> CheckUserExists(string username);

        // Update
        Task<User> UpdateAsync(User user);
        Task<User> UpdateEmailAsync(string username, string newEmail);
        Task<User> UpdateStatusAsync(string username, UserStatus newStatus);
        Task<User> UpdateCharacterIds(User user, List<string> characterIds);
        Task<User> UpdateCampaignIds(User user, List<string> campaignIds);
        Task<User> RefreshLastLogin(string username);

        // Delete
        Task<bool> DeleteAsync(string id);
        Task<bool> LogicDeleteAsync(string id);
    }
}