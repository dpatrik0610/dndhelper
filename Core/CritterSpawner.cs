using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Core
{
    public class CritterSpawner : BackgroundService
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
        private const int MinWaitSeconds = 180;
        private const int MaxWaitSeconds = 360;
        private const int GoldenOdds = 50;

        private sealed record Spawn(string Id, bool Golden, DateTime ExpiresAt);

        private readonly IHubContext<NotificationHub> _hub;
        private Spawn? _current;

        public CritterSpawner(IHubContext<NotificationHub> hub)
        {
            _hub = hub;
        }

        public bool TryClaim(string spawnId, bool golden)
        {
            var spawn = Volatile.Read(ref _current);
            return spawn != null
                && spawn.Id == spawnId
                && spawn.Golden == golden
                && spawn.ExpiresAt > DateTime.UtcNow
                && Interlocked.CompareExchange(ref _current, null, spawn) == spawn;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(MinWaitSeconds, MaxWaitSeconds)), stoppingToken);
                var spawn = new Spawn(Guid.NewGuid().ToString("N"), Random.Shared.Next(GoldenOdds) == 0, DateTime.UtcNow + Lifetime);
                Volatile.Write(ref _current, spawn);
                await _hub.Clients.All.SendAsync("CritterSpawned", spawn.Id, spawn.Golden, stoppingToken);
            }
        }
    }
}
