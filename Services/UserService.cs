using dndhelper.Authentication;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Serilog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly ILogger _logger;
        public UserService(IUserRepository userRepository, ILogger logger) 
        {
            _userRepository = userRepository ?? throw CustomExceptions.ThrowArgumentNullException(Log.Logger, nameof(userRepository));
            _logger = logger ?? throw CustomExceptions.ThrowApplicationException(Log.Logger, nameof(logger));
        }

        // Create
        public async Task<User> CreateAsync(User user)
        {
            if (user == null)
                throw CustomExceptions.ThrowArgumentNullException(_logger, nameof(user));

            if (string.IsNullOrWhiteSpace(user.Username) || string.IsNullOrWhiteSpace(user.PasswordHash))
                throw CustomExceptions.ThrowArgumentException(_logger, "Mandatory user fields missing");

            if (await _userRepository.CheckUserExists(user.Username))
                throw CustomExceptions.ThrowInvalidOperationException(_logger, "User already exists");

            await _userRepository.CreateAsync(user);
            var createdUser = await _userRepository.GetByIdAsync(user.Id);
            return createdUser;
        }

        // Read
        public async Task<User> GetSelfAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(userId));

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                throw CustomExceptions.ThrowNotFoundException(_logger, nameof(userId));

            return user;
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(username));

            var user = await _userRepository.GetByUsernameAsync(username);
            return user ?? throw CustomExceptions.ThrowNotFoundException(_logger, nameof(username));
        }

        public async Task<User?> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(id));

            var user = await _userRepository.GetByIdAsync(id);
            return user ?? throw CustomExceptions.ThrowNotFoundException(_logger, nameof(id));
        }

        public async Task<List<User>> GetAllAsync()
        {
            return await _userRepository.GetAllAsync();
        }

        public async Task<bool> CheckUserExists(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(username));
            return await _userRepository.CheckUserExists(username);
        }

        // Update
        public async Task<User> UpdateAsync(User user)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Id))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(user));

            var existing = GetByIdAsync(user.Id);

            await _userRepository.UpdateAsync(user);
            return await _userRepository.GetByIdAsync(user.Id);
        }

        public async Task<User> UpdateEmailAsync(string username, string newEmail)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(newEmail))
                throw CustomExceptions.ThrowArgumentException(_logger, "Username or new email is empty");

            var user = await GetByUsernameAsync(username);
            
            user!.Email = newEmail;
            await _userRepository.UpdateAsync(user);
            _logger.Information($"Email updated for user: {username}");
            return await _userRepository.GetByIdAsync(user.Id);
        }

        public async Task<User> UpdateStatusAsync(string username, UserStatus newStatus)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(username));

            var user = await GetByUsernameAsync(username);

            user!.IsActive = newStatus;
            await _userRepository.UpdateAsync(user);
            _logger.Information($"Status updated for user: {username}");
            return await _userRepository.GetByIdAsync(user.Id);
        }

        public async Task<User> UpdateCharacterIds(User user, List<string> characterIds)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Id))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(user));
            await _userRepository.UpdateCharacterIds(user, characterIds ?? new List<string>());
            return await _userRepository.GetByIdAsync(user.Id);
        }

        public async Task<User> UpdateCampaignIds(User user, List<string> campaignIds)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Id))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(user));
            await _userRepository.UpdateCampaignIds(user, campaignIds ?? new List<string>());
            return await _userRepository.GetByIdAsync(user.Id);
        }

        public async Task<User> RefreshLastLogin(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(username));
            await _userRepository.RefreshLastLogin(username);
            return await _userRepository.GetByUsernameAsync(username);
        }

        // Delete
        public async Task<bool> DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(id));

            var user = await GetByIdAsync(id);

            await _userRepository.DeleteAsync(id);
            return true;
        }

        public async Task<bool> LogicDeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(id));

            var user = await GetByIdAsync(id);

            await _userRepository.LogicDeleteAsync(user!);
            return true;
        }
    }
}
