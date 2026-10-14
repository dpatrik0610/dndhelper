using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.RateLimiting;

namespace dndhelper.Authentication
{
    /// <summary>
    /// Per-IP limits on the anonymous auth endpoints (first layer; LoginThrottle guards each account).
    /// Behind a reverse proxy set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, or every user shares the proxy's IP and one limit.
    /// </summary>
    public static class AuthRateLimits
    {
        public const string Login = "auth-login";
        public const string Register = "auth-register";
        public const string CritterCatch = "critter-catch";

        public static IServiceCollection AddAuthRateLimits(this IServiceCollection services) =>
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, ct) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                        context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new { message = "Too many attempts. Please wait a moment and try again." }, ct);
                };

                // 10 sign-in attempts per minute per IP; a whole group at one table stays well under it.
                options.AddPolicy(Login, http => RateLimitPartition.GetSlidingWindowLimiter(ClientIp(http),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    }));

                options.AddPolicy(Register, http => RateLimitPartition.GetFixedWindowLimiter(ClientIp(http),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                    }));

                // Critter catches feed the scoreboard: a critter shows up every few minutes, so 6 a minute per IP
                // covers a whole table of players and stops a script from flooding it. (Per IP, not per user: this
                // middleware runs before authentication.)
                options.AddPolicy(CritterCatch, http => RateLimitPartition.GetFixedWindowLimiter(ClientIp(http),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 6,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
            });

        private static string ClientIp(HttpContext http) =>
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
