using dndhelper.Authentication;
using dndhelper.Models.DTOs;
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
        Task<User?> UpdateCharacterIds(User user, List<string> characterIds);
        Task<User?> UpdateCampaignIds(User user, List<string> campaignIds);
        Task<User?> RefreshLastLogin(string username);
        Task<User?> UpdateSettingsForUser(User user, Dictionary<string, string> settings);

        // Superadmin user management
        Task<List<AdminUserDto>> GetAdminOverviewAsync();
        Task<AdminUserDto> AdminCreateAsync(AdminUserRequest request);
        Task<AdminUserDto> AdminUpdateAsync(string id, AdminUserRequest request);
        Task<bool> AdminDeleteAsync(string id);
    }
}