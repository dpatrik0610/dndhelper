using Microsoft.Extensions.Caching.Memory;
using System;

namespace dndhelper.Authentication
{
    /// <summary>
    /// Per-account brute-force guard: 5 failed sign-ins without a 15-minute break lock that username for 15 minutes.
    /// Catches guessing spread over many IPs, which the per-IP limit can't. Keyed by the typed username whether or not
    /// the account exists, so a lock never reveals which accounts are real.
    /// ponytail: in-memory, so it resets on restart and isn't shared between instances; move to Mongo/Redis if the API scales out.
    /// </summary>
    public class LoginThrottle
    {
        public const int MaxFailures = 5;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

        // Own cache: entries expire on their own, and the app cache's logging stays out of it.
        private readonly MemoryCache _attempts = new(new MemoryCacheOptions());

        private sealed class Attempts
        {
            public int Failures;
            public DateTimeOffset? LockedUntil;
        }

        /// <summary>Time left on the lock, or null when sign-in may be tried.</summary>
        public TimeSpan? LockedFor(string username)
        {
            if (!_attempts.TryGetValue(Key(username), out Attempts? entry)) return null;
            lock (entry!)
            {
                var left = entry.LockedUntil - DateTimeOffset.UtcNow;
                return left > TimeSpan.Zero ? left : null;
            }
        }

        public void Failed(string username)
        {
            var key = Key(username);
            var entry = _attempts.GetOrCreate(key, _ => new Attempts())!;
            lock (entry)
            {
                entry.Failures++;
                if (entry.Failures >= MaxFailures)
                {
                    entry.LockedUntil = DateTimeOffset.UtcNow + Window;
                    entry.Failures = 0;
                }
            }
            _attempts.Set(key, entry, Window);
        }

        public void Succeeded(string username) => _attempts.Remove(Key(username));

        private static string Key(string username) => username.Trim().ToLowerInvariant();
    }
}
