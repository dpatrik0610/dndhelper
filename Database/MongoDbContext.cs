using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Database
{
    public class MongoDbContext
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly ILogger _logger;
        private readonly string _databaseName;
        private readonly string _connectionString;

        public string DatabaseName => _databaseName;
        public string ConnectionString => _connectionString;

        public MongoDbContext(string connectionString, string databaseName, ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseName = databaseName ?? throw new ArgumentNullException(nameof(databaseName));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

            try
            {
                _client = new MongoClient(_connectionString);
                _database = _client.GetDatabase(_databaseName);
                _logger.Information("MongoDB client initialized for database {DbName}", _databaseName);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "?? Couldn't initialize MongoDB client/database.");
                throw;
            }
        }

        public IMongoCollection<T> GetCollection<T>(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                throw new ArgumentNullException(nameof(collectionName));

            return _database.GetCollection<T>(collectionName);
        }

        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _database.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }", cancellationToken: cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "MongoDB ping failed for database {DbName}", _databaseName);
                return false;
            }
        }
    }
}
