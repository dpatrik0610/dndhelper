using dndhelper.Authentication;
using dndhelper.Models.DTOs;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class UserService : BaseService<User, IUserRepository>, IUserService
    {
        private readonly ICharacterRepository _characterRepository;
        private readonly ICampaignRepository _campaignRepository;

        public UserService(IUserRepository repository, ILogger logger, IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor, ICharacterRepository characterRepository, ICampaignRepository campaignRepository)
            : base(repository, logger, authorizationService, httpContextAccessor)
        {
            _characterRepository = Guard.NotNull(characterRepository, nameof(characterRepository));
            _campaignRepository = Guard.NotNull(campaignRepository, nameof(campaignRepository));
        }

        // ------------------------
        // SUPERADMIN USER MANAGEMENT (UserController restricts these to the Admin role)
        // ------------------------

        /// <summary>Every user with their characters and campaign memberships, without password hashes.</summary>
        // ponytail: loads every user, character and campaign per call; page it when the site outgrows that.
        public async Task<List<AdminUserDto>> GetAdminOverviewAsync()
        {
            var users = await _repository.GetAllAsync();
            var characters = await _characterRepository.GetAllAsync();
            var campaigns = await _campaignRepository.GetAllAsync();

            var charactersByOwner = characters
                .SelectMany(c => (c.OwnerIds ?? new List<string>()).Select(ownerId => (ownerId, c)))
                .ToLookup(x => x.ownerId, x => new AdminUserCharacterDto
                {
                    Id = x.c.Id!,
                    Name = x.c.Name,
                    CharacterClass = x.c.CharacterClass,
                    Level = x.c.Level,
                    CampaignId = x.c.CampaignId,
                });
            var campaignsByMember = campaigns
                .SelectMany(c => c.Members.Select(m => (m.UserId, dto: new AdminUserCampaignDto { Id = c.Id!, Name = c.Name, Roles = m.Roles })))
                .ToLookup(x => x.UserId, x => x.dto);

            return users
                .OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
                .Select(u => ToAdminDto(u, charactersByOwner[u.Id].ToList(), campaignsByMember[u.Id].ToList()))
                .ToList();
        }

        public async Task<AdminUserDto> AdminCreateAsync(AdminUserRequest request)
        {
            var username = request.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
                throw new ArgumentException("Username and password are required.");
            if (await _repository.CheckUserExists(username))
                throw new AlreadyExistsException($"The username '{username}' is taken.");

            var user = new User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Email = request.Email?.Trim() ?? string.Empty,
                Roles = NormalizeRoles(request.Roles),
                IsActive = request.Status ?? UserStatus.Active,
                DateCreated = DateTime.UtcNow,
            };
            await _repository.CreateAsync(user);
            _logger.Information("Superadmin created user {Username}", username);
            return ToAdminDto(user, new(), new());
        }

        /// <summary>Changes only the fields the request carries. The superadmin can't lock themselves out.</summary>
        public async Task<AdminUserDto> AdminUpdateAsync(string id, AdminUserRequest request)
        {
            var user = await _repository.GetByIdAsync(id) ?? throw new NotFoundException("User not found.");
            var isSelf = id == Access.UserId;

            var username = request.Username?.Trim();
            if (!string.IsNullOrEmpty(username) && username != user.Username)
            {
                if (await _repository.CheckUserExists(username))
                    throw new AlreadyExistsException($"The username '{username}' is taken.");
                user.Username = username;
            }
            if (request.Email != null)
                user.Email = request.Email.Trim();
            if (request.Roles != null)
            {
                if (isSelf && !request.Roles.Contains(UserRole.Admin))
                    throw new InvalidOperationException("You can't remove your own superadmin role.");
                user.Roles = NormalizeRoles(request.Roles);
            }
            if (request.Status is { } status)
            {
                if (isSelf && status != UserStatus.Active)
                    throw new InvalidOperationException("You can't deactivate or ban yourself.");
                user.IsActive = status;
            }
            if (!string.IsNullOrWhiteSpace(request.Password))
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            user.UpdatedAt = DateTime.UtcNow;
            await _repository.UpdateAsync(user);
            _logger.Information("Superadmin updated user {Username}", user.Username);

            // Characters and campaigns don't change here; the client keeps the ones it has.
            return ToAdminDto(user, new(), new());
        }

        public async Task<bool> AdminDeleteAsync(string id)
        {
            if (id == Access.UserId)
                throw new InvalidOperationException("You can't delete your own account.");
            return await _repository.DeleteAsync(id);
        }

        private static List<UserRole> NormalizeRoles(List<UserRole>? roles) =>
            roles is { Count: > 0 } ? roles.Distinct().ToList() : new List<UserRole> { UserRole.User };

        private static AdminUserDto ToAdminDto(User u, List<AdminUserCharacterDto> characters, List<AdminUserCampaignDto> campaigns) => new()
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            Roles = u.Roles,
            Status = u.IsActive,
            DateCreated = u.DateCreated,
            LastLogin = u.LastLogin,
            Characters = characters,
            Campaigns = campaigns,
        };

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

        public async Task<User?> UpdateCharacterIds(User user, List<string> characterIds)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Id))
                throw CustomExceptions.ThrowArgumentException(_logger, nameof(user));
            await _repository.UpdateCharacterIds(user, characterIds ?? new List<string>());
            return await _repository.GetByIdAsync(user.Id);
        }

        public async Task<User?> UpdateSettingsForUser(User user, Dictionary<string,string> settings)
        {
            Guard.NotNull(user, nameof(user));
            Guard.NotNullOrWhiteSpace(user.Id, nameof(user.Id));

            try
            {
                user.Settings = settings;
                var response = await _repository.UpdateAsync(user);
                if (response != null) return user;
            }
            catch (Exception ex)
            {
                throw CustomExceptions.ThrowApplicationException(_logger, $"Server Error during updating settings: {ex}");
            }
            return null;
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
