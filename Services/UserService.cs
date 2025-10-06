using dndhelper.Authentication;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Serilog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class UserService : BaseService<User, IUserRepository>, IUserService
    {
        public UserService(IUserRepository repository, ILogger logger) : base(repository, logger)
        {
        }

        // Create
        public override async Task<User?> CreateAsync(User user)
        {
            if (user == null)
                throw CustomExceptions.ThrowArgumentNullException(_logger, nameof(user));

            if (string.IsNullOrWhiteSpace(user.Username) || string.IsNullOrWhiteSpace(user.PasswordHash))
                throw CustomExceptions.ThrowArgumentException(_logger, "Mandatory user fields missing");

            if (await _repository.CheckUserExists(user.Username))
                throw CustomExceptions.ThrowInvalidOperationException(_logger, "User already exists");

            await _repository.CreateAsync(user);
            var createdUser = await _repository.GetByIdAsync(user.Id);
            return createdUser;
        }

        // Read
        public async Task<User> GetSelfAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(userId));

            var user = await _repository.GetByIdAsync(userId);
            if (user == null)
                throw CustomExceptions.ThrowNotFoundException(_logger, nameof(userId));

            return user;
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(username));

            var user = await _repository.GetByUsernameAsync(username);
            return user ?? throw CustomExceptions.ThrowNotFoundException(_logger, nameof(username));
        }

        public async Task<bool> CheckExistsByUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(username));
            return await _repository.CheckUserExists(username);
        }

        public async Task<User?> UpdateEmailAsync(string username, string newEmail)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(newEmail))
                throw CustomExceptions.ThrowArgumentException(_logger, "Username or new email is empty");

            var user = await GetByUsernameAsync(username);
            
            user!.Email = newEmail;
            await _repository.UpdateAsync(user);

            _logger.Information($"Email updated for user: {username}");
            return await _repository.GetByIdAsync(user.Id);
        }

        public async Task<User?> UpdateStatusAsync(string username, UserStatus newStatus)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(username));

            var user = await GetByUsernameAsync(username);

            user!.IsActive = newStatus;
            await _repository.UpdateAsync(user);
            _logger.Information($"Status updated for user: {username}");
            return await _repository.GetByIdAsync(user.Id);
        }

        public async Task<User?> UpdateCharacterIds(User user, List<string> characterIds)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Id))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(user));
            await _repository.UpdateCharacterIds(user, characterIds ?? new List<string>());
            return await _repository.GetByIdAsync(user.Id);
        }

        public async Task<User?> UpdateCampaignIds(User user, List<string> campaignIds)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Id))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(user));
            await _repository.UpdateCampaignIds(user, campaignIds ?? new List<string>());
            return await _repository.GetByIdAsync(user.Id);
        }

        public async Task<User?> RefreshLastLogin(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(username));
            await _repository.RefreshLastLogin(username);
            return await _repository.GetByUsernameAsync(username);
        }
    }
}
