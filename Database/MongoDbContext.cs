using MongoDB.Driver;
using Serilog;
using System;

namespace dndhelper.Database
{
    public class MongoDbContext : IDisposable
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly ILogger _logger;

        #pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public MongoDbContext(string connectionString, string databaseName, ILogger logger)
        #pragma warning restore CS8618 
        {
            _logger = logger;
            try
            {
                _client = new MongoClient(connectionString) ?? throw new ArgumentNullException(nameof(connectionString));
                _database = _client.GetDatabase(databaseName) ?? throw new ArgumentNullException(nameof(databaseName));
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "🔥 Couldn't establish communication with the database.");
            }

            _logger.Information("Connected to Database successfully! ✨");
        }

        public IMongoCollection<T> GetCollection<T>(string collectionName)
        {
            return _database.GetCollection<T>(collectionName);
        }

        public void Dispose()
        {
            _logger.Information("Closing Database Connection 💀");
            GC.SuppressFinalize(this);
        }
    }
}