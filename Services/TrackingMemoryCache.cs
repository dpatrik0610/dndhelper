using Microsoft.Extensions.Caching.Memory;

namespace dndhelper.Services
{
    public class TrackingMemoryCache : IMemoryCache
    {
        private readonly IMemoryCache _inner;

        public TrackingMemoryCache(IMemoryCache inner)
        {
            _inner = inner;
        }

        public ICacheEntry CreateEntry(object key)
        {
            CacheKeyStore.Add(key.ToString()!);
            return _inner.CreateEntry(key);
        }

        public void Remove(object key)
        {
            CacheKeyStore.Remove(key.ToString()!);
            _inner.Remove(key);
        }

        public bool TryGetValue(object key, out object value)
        {
            return _inner.TryGetValue(key, out value);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}
