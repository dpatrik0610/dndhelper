using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;

namespace dndhelper.Database
{
    public class MongoDbContext : IDisposable
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase? _database;
        private readonly ILogger _logger;
        public bool IsConnected { get; private set; } = false;

        public MongoDbContext(string connectionString, string databaseName, ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            try
            {
                _client = new MongoClient(connectionString ?? throw new ArgumentNullException(nameof(connectionString)));
                _database = _client.GetDatabase(databaseName ?? throw new ArgumentNullException(nameof(databaseName)));

                var collections = new List<string>();
                try
                {
                    collections = _database.ListCollectionNames()
                                           .ToList();
                    IsConnected = true;
                    _logger.Information("Connected to Database successfully! ✨");
                    _logger.Information($"Collections: {string.Join(", ", collections)}");
                }
                catch (TimeoutException tex)
                {
                    IsConnected = false;
                    _logger.Warning(tex, "Timeout while listing collections — MongoDB might be unreachable.");
                }
                catch (Exception ex)
                {
                    IsConnected = false;
                    _logger.Warning(ex, "Unexpected error while testing MongoDB connection.");
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                _logger.Warning(ex, "🔥 Couldn't establish communication with the database.");
            }
        }

        public IMongoCollection<T> GetCollection<T>(string collectionName)
        {
            if (!IsConnected || _database == null)
                throw new InvalidOperationException("Database connection is not established.");

            return _database.GetCollection<T>(collectionName);
        }

        public void Dispose()
        {
            _logger.Information("Closing Database Connection 💀");
            GC.SuppressFinalize(this);
        }
    }
}
