using dndhelper.Authentication.Interfaces;
using dndhelper.Database;
using MongoDB.Driver;
using Serilog;
using System.Threading.Tasks;

namespace dndhelper.Authentication
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
            _logger.Debug("Fetching user by username: {Username}", username);
            return await _users.Find(u => u.Username == username).FirstOrDefaultAsync();
        }

        public async Task CreateAsync(User user)
        {
            _logger.Debug("Creating new user: {Username}", user.Username);
            await _users.InsertOneAsync(user);
        }
    }
}