using dndhelper.Authentication.Interfaces;
using dndhelper.Services;
using dndhelper.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace dndhelper.Authentication
{
    /// <summary>
    /// Provides authentication services for users.
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly IUserService _userService;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger _logger;

        public AuthService(IUserService userService, IConfiguration config, IHttpContextAccessor httpContextAccessor, ILogger logger)
        {
            _logger = logger ?? throw CustomExceptions.ThrowArgumentNullException(Log.Logger, nameof(logger));
            _config = config ?? throw CustomExceptions.ThrowArgumentNullException(_logger, nameof(config));
            _userService = userService ?? throw CustomExceptions.ThrowArgumentNullException(_logger, nameof(userService));
            _httpContextAccessor = httpContextAccessor ?? throw CustomExceptions.ThrowArgumentNullException(_logger, nameof(httpContextAccessor));
        }

        /// <summary>
        /// Authenticates a user and returns a JWT token.
        /// </summary>
        public async Task<string> AuthenticateAsync(string username, string password)
        {
            ValidateCredentials(username, password);

            var user = await _userService.GetByUsernameAsync(username);
            if (!IsValidUser(user, password))
            {
                CustomExceptions.ThrowUnauthorizedAccessException(_logger, nameof(username));
            }

            var token = GenerateJwtToken(user);
            _logger.Information($"Token generated for user: {username}");
            return token;
        }

        /// <summary>
        /// Registers a new user.
        /// </summary>
        public async Task RegisterAsync(string username, string password)
        {
            ValidateCredentials(username, password);

            if (await _userService.CheckUserExists(username))
            {
                _logger.Warning($"User already exists with username: {username}");
                CustomExceptions.ThrowInvalidOperationException(_logger, nameof(username));
            }

            var user = CreateNewUser(username, password);
            await _userService.CreateAsync(user);
            _logger.Information($"User registered successfully: {username}");
        }

        /// <summary>
        /// Retrieves the user ID from the current JWT token.
        /// </summary>
        public string GetUserIdFromToken()
        {
            var userId = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _logger.Information($"Retrieved user ID: {userId ?? "null"} from token.");
            return userId ?? string.Empty;
        }

        /// <summary>
        /// Retrieves the user object from the current JWT token.
        /// </summary>
        public async Task<User> GetUserFromTokenAsync()
        {
            var userId = GetUserIdFromToken();
            if (string.IsNullOrWhiteSpace(userId))
            {
                CustomExceptions.ThrowUnauthorizedAccessException(_logger, nameof(userId));
            }

            var user = await _userService.GetByIdAsync(userId);
            if (user == null)
            {
                _logger.Warning($"Could not find user with ID: {userId}");
                CustomExceptions.ThrowNotFoundException(_logger, nameof(userId));
            }
            _logger.Information($"User retrieved from token: {user.Username}");
            return user;
        }

        // --- Private Helpers ---

        private void ValidateCredentials(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
                CustomExceptions.ThrowArgumentException(_logger, nameof(username));
            if (string.IsNullOrWhiteSpace(password))
                CustomExceptions.ThrowArgumentException(_logger, nameof(password));
        }

        private bool IsValidUser(User? user, string password)
        {
            return user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        }

        private string GenerateJwtToken(User user)
        {
            var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured."));
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.Role, string.IsNullOrWhiteSpace(user.Role.ToString()) ? "Player" : user.Role.ToString()),
                new Claim("IsActive", user.IsActive.ToString())
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                Issuer = _config["Jwt:Issuer"],
                Audience = _config["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private User CreateNewUser(string username, string password)
        {
            return new User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Email = string.Empty,
                Role = UserRole.Player,
                ProfilePictureUrl = null,
                DateCreated = DateTime.UtcNow,
                LastLogin = null,
                CharacterIds = new List<string>(),
                CampaignIds = new List<string>(),
                IsActive = UserStatus.Active,
                Settings = new Dictionary<string, string>()
            };
        }
    }
}