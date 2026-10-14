using dndhelper.Authentication;
using dndhelper.Database;
using dndhelper.Repositories.Interfaces;
using dndhelper.Utils;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class UserRepository : MongoRepository<User>, IUserRepository
    {
        public UserRepository(MongoDbContext context, ILogger logger) : base(logger, null!, context, "Users")
        {
            if (_indexesCreated) return;
            // ponytail: not unique, existing data may hold duplicates; make it unique once they're cleaned up.
            TryCreateIndexes(new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Username),
                new CreateIndexOptions { Name = "idx_users_username" }));
            _indexesCreated = true;
        }

        private static bool _indexesCreated;

        public async Task<User> GetByUsernameAsync(string username)
        {
            try
            {
                _logger.Debug("Fetching user by username: {Username}", username);
                var user = await _collection.Find(u => u.Username == username).FirstOrDefaultAsync();
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


        public async Task<bool> CheckUserExists(string username)
        {
            try
            {
                _logger.Debug("Checking if user exists: {Username}", username);
                return await _collection.CountDocumentsAsync(u => u.Username == username) != 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking if user exists: {Username}", username);
                throw;
            }
        }

        public async Task LogicDeleteAsync(User user)
        {
            try
            {
                _logger.Debug("Logic deleting user: {Id}", user.Id);
                var update = Builders<User>.Update.Set(u => u.IsActive, UserStatus.LogicDeleted);
                var result = await _collection.UpdateOneAsync(u => u.Id == user.Id, update);
                RemoveFromCache(user.Id!);
                if (result.MatchedCount == 0)
                    throw CustomExceptions.ThrowNotFoundException(_logger, nameof(user.Id));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error logic deleting user: {Id}", user.Id);
                throw;
            }
        }

        public async Task<User> IncrementCritterAsync(string userId, string critter)
        {
            // $inc on one key: catches arriving together can't overwrite each other. The service only passes
            // known critter names, since the name becomes part of the field path.
            var update = Builders<User>.Update.Inc($"{nameof(User.CrittersCaught)}.{critter}", 1);
            var options = new FindOneAndUpdateOptions<User>
            {
                ReturnDocument = ReturnDocument.After,
                Projection = Builders<User>.Projection.Include(u => u.Username).Include(u => u.CrittersCaught),
            };
            var user = await _collection.FindOneAndUpdateAsync<User>(u => u.Id == userId, update, options);
            RemoveFromCache(userId);
            if (user == null)
                throw CustomExceptions.ThrowNotFoundException(_logger, nameof(userId));
            return user;
        }

        public async Task<List<User>> GetCritterCatchersAsync()
        {
            var f = Builders<User>.Filter;
            var filter = f.Ne(u => u.IsDeleted, true)
                & f.Ne(u => u.IsActive, UserStatus.LogicDeleted)
                & f.Exists(nameof(User.CrittersCaught))
                & f.Ne(nameof(User.CrittersCaught), new BsonDocument());
            return await _collection.Find(filter)
                .Project<User>(Builders<User>.Projection.Include(u => u.Username).Include(u => u.CrittersCaught))
                .ToListAsync();
        }

        public async Task AddCharacterIdAsync(string userId, string characterId)
        {
            // $addToSet: concurrent creates can't overwrite each other's ids.
            var update = Builders<User>.Update.AddToSet(u => u.CharacterIds, characterId);
            await _collection.UpdateOneAsync(u => u.Id == userId, update);
            RemoveFromCache(userId);
        }

        public async Task RemoveCharacterIdAsync(string userId, string characterId)
        {
            var update = Builders<User>.Update.Pull(u => u.CharacterIds, characterId);
            await _collection.UpdateOneAsync(u => u.Id == userId, update);
            RemoveFromCache(userId);
        }

        public async Task UpdateCharacterIds(User user, List<string> characterIds)
        {
            try
            {
                _logger.Debug("Updating character IDs for user: {Id}", user.Id);
                var update = Builders<User>.Update.Set(u => u.CharacterIds, characterIds);
                var result = await _collection.UpdateOneAsync(u => u.Id == user.Id, update);
                RemoveFromCache(user.Id!);
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
                var result = await _collection.UpdateOneAsync(u => u.Id == user.Id, update);
                RemoveFromCache(user.Id!);
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
                var result = await _collection.UpdateOneAsync(u => u.Username == username, update);
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