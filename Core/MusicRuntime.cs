using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Core
{
    public sealed class MusicRuntime
    {
        private const long PresentMs = 90_000;

        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long>> _seen = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger _logger;

        public MusicRuntime(IServiceScopeFactory scopes, ILogger logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public async Task<IDisposable> LockAsync(string campaignId)
        {
            var gate = _locks.GetOrAdd(campaignId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            return new Release(gate);
        }

        public void Here(string campaignId, string userId) =>
            _seen.GetOrAdd(campaignId, _ => new())[userId] = Now;

        public int PresentOthers(string campaignId, string userId)
        {
            if (!_seen.TryGetValue(campaignId, out var seen)) return 0;
            var since = Now - PresentMs;
            return seen.Count(kv => kv.Key != userId && kv.Value >= since);
        }

        public void WakeAt(string campaignId, long? atMs)
        {
            var cts = new CancellationTokenSource();
            if (_timers.TryRemove(campaignId, out var old)) old.Cancel();
            if (atMs == null) return;
            _timers[campaignId] = cts;
            var delay = TimeSpan.FromMilliseconds(Math.Max(0, atMs.Value - Now));
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token);
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<Services.MusicService>().TickAsync(campaignId);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.Warning(ex, "Music tick failed for campaign {CampaignId}", campaignId); }
            });
        }

        private sealed class Release : IDisposable
        {
            private SemaphoreSlim? _gate;
            public Release(SemaphoreSlim gate) => _gate = gate;
            public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}
