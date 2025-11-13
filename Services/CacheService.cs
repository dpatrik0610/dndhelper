using dndhelper.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using System.Collections.Generic;
using System.Linq;

namespace dndhelper.Services
{
    public class CacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger _logger;

        public CacheService(IMemoryCache cache, ILogger logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public void TrackKey(string key)
        {
            CacheKeyStore.Add(key);
        }

        public void RemoveTracked(string key)
        {
            CacheKeyStore.Remove(key);
        }

        public List<string> GetAllKeys()
        {
            return CacheKeyStore.Keys.ToList();
        }

        public void ClearAllTracked()
        {
            CacheKeyStore.Clear();
        }
        public void ClearAllFromCache()
        {
            foreach (var key in CacheKeyStore.Keys)
            {
                _cache.Remove(key);
                _logger.Information("🔴 [CACHE REMOVE] {Key}", key);
            }
        }
    }
}
