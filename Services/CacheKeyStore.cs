using System.Collections.Concurrent;
using System.Collections.Generic;

namespace dndhelper.Services
{
    public static class CacheKeyStore
    {
        private static readonly ConcurrentDictionary<string, byte> _keys = new();

        public static void Add(string key) => _keys[key] = 0;
        public static void Remove(string key) => _keys.TryRemove(key, out _);
        public static void Clear() => _keys.Clear();

        public static IEnumerable<string> Keys => _keys.Keys;
    }
}
