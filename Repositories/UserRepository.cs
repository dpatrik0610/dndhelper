using dndhelper.Authentication;
using dndhelper.Database;
using dndhelper.Repositories.Interfaces;
using dndhelper.Utils;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly IMongoCollection<User> _users;
        private readonly ILogger _logger;

        public UserRepository(MongoDbContext context, ILogger logger)
        {
            _users = context.GetCollection<User>("Users");
            _logger = logger;
        }

        public async Task<User> GetByUsernameAsync(string username)
        {
            try
            {
                _logger.Debug("Fetching user by username: {Username}", username);
                var user = await _users.Find(u => u.Username == username).FirstOrDefaultAsync();
                if (user == null)
                    throw CustomExceptions.ThrowNotFoundException(_logger, nameof(username));
                return user;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching user by username: {Username}", username);
                throw;
            }
        }

        public async Task<User> GetByIdAsync(string id)
        {
            try
            {
                _logger.Debug("Fetching user by id: {Id}", id);
                var user = await _users.Find(u => u.Id == id).FirstOrDefaultAsync();
                if (user == null)
                    throw CustomExceptions.ThrowNotFoundException(_logger, nameof(id));
                return user;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching user by id: {Id}", id);
                throw;
            }
        }

        public async Task<List<User>> GetAllAsync()
        {
            try
            {
                _logger.Debug("Fetching all users");
                return await _users.Find(_ => true).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching all users");
                throw;
            }
        }

        public async Task CreateAsync(User user)
        {
            try
            {
                _logger.Debug("Creating new user: {Username}", user.Username);
                await _users.InsertOneAsync(user);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.Error(ex, "Duplicate user creation attempt: {Username}", user.Username);
                throw CustomExceptions.ThrowInvalidOperationException(_logger, nameof(user.Username));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating user: {Username}", user.Username);
                throw;
            }
        }

        public async Task<bool> CheckUserExists(string username)
        {
            try
            {
                _logger.Debug("Checking if user exists: {Username}", username);
                return await _users.CountDocumentsAsync(u => u.Username == username) != 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking if user exists: {Username}", username);
                throw;
            }
        }

        public async Task UpdateAsync(User user)
        {
            try
            {
                _logger.Debug("Updating user: {Id}", user.Id);
                var result = await _users.ReplaceOneAsync(u => u.Id == user.Id, user);
                if (result.MatchedCount == 0)
                    throw CustomExceptions.ThrowNotFoundException(_logger, nameof(user.Id));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating user: {Id}", user.Id);
                throw;
            }
        }

        public async Task DeleteAsync(string id)
        {
            try
            {
                _logger.Debug("Deleting user: {Id}", id);
                var result = await _users.DeleteOneAsync(u => u.Id == id);
                if (result.DeletedCount == 0)
                    throw CustomExceptions.ThrowNotFoundException(_logger, nameof(id));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting user: {Id}", id);
                throw;
            }
        }

        public async Task LogicDeleteAsync(User user)
        {
            try
            {
                _logger.Debug("Logic deleting user: {Id}", user.Id);
                var update = Builders<User>.Update.Set(u => u.IsActive, UserStatus.LogicDeleted);
                var result = await _users.UpdateOneAsync(u => u.Id == user.Id, update);
                if (result.MatchedCount == 0)
                    throw CustomExceptions.ThrowNotFoundException(_logger, nameof(user.Id));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error logic deleting user: {Id}", user.Id);
                throw;
            }
        }

        public async Task UpdateCharacterIds(User user, List<string> characterIds)
        {
            try
            {
                _logger.Debug("Updating character IDs for user: {Id}", user.Id);
                var update = Builders<User>.Update.Set(u => u.CharacterIds, characterIds);
                var result = await _users.UpdateOneAsync(u => u.Id == user.Id, update);
                if (result.MatchedCount == 0)
                    throw CustomExceptions.ThrowNotFoundException(_logger, nameof(user.Id));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating character IDs for user: {Id}", user.Id);
                throw;
            }
        }

        public async Task UpdateCampaignIds(User user, List<string> campaignIds)
        {
            try
            {
                _logger.Debug("Updating campaign IDs for user: {Id}", user.Id);
                var update = Builders<User>.Update.Set(u => u.CampaignIds, campaignIds);
                var result = await _users.UpdateOneAsync(u => u.Id == user.Id, update);
                if (result.MatchedCount == 0)
                    throw CustomExceptions.ThrowNotFoundException(_logger, nameof(user.Id));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating campaign IDs for user: {Id}", user.Id);
                throw;
            }
        }

        public async Task RefreshLastLogin(string username)
        {
            try
            {
                _logger.Debug("Refreshing last login for user: {Username}", username);
                var update = Builders<User>.Update.Set(u => u.LastLogin, DateTime.UtcNow);
                var result = await _users.UpdateOneAsync(u => u.Username == username, update);
                if (result.MatchedCount == 0)
                    throw CustomExceptions.ThrowNotFoundException(_logger, nameof(username));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error refreshing last login for user: {Username}", username);
                throw;
            }
        }
    }
}