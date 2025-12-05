using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Database
{
    public class MongoHealthCheck : IHealthCheck
    {
        private readonly MongoDbContext _context;

        public MongoHealthCheck(MongoDbContext context)
        {
            _context = context;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var healthy = await _context.IsHealthyAsync(cancellationToken);

            return healthy
                ? HealthCheckResult.Healthy("MongoDB reachable")
                : HealthCheckResult.Unhealthy("MongoDB unreachable");
        }
    }
}
