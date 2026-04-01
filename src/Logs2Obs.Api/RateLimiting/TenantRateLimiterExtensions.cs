using System.Threading.RateLimiting;
using Logs2Obs.Api.Auth;
using Microsoft.AspNetCore.RateLimiting;
using ApiRateLimiterOptions = Logs2Obs.Api.Options.RateLimiterOptions;

namespace Logs2Obs.Api.RateLimiting;

/// <summary>
/// Extension methods for registering per-tenant rate limiting policies.
/// </summary>
public static class TenantRateLimiterExtensions
{
    /// <summary>
    /// Registers the ASP.NET Core rate limiter with per-tenant token-bucket (ingest)
    /// and sliding-window (query) policies, reading limits from the
    /// <c>RateLimiter</c> configuration section.
    /// </summary>
    public static IServiceCollection AddTenantRateLimiting(
        this IServiceCollection services,
        IConfiguration config)
    {
        var opts = config.GetSection("RateLimiter").Get<ApiRateLimiterOptions>() ?? new ApiRateLimiterOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("tenant-ingest", context =>
            {
                var partitionKey = context.GetTenantId() ??
                                   context.Connection.RemoteIpAddress?.ToString() ??
                                   "unknown";

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = opts.IngestTokenLimit,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    TokensPerPeriod = opts.IngestTokensPerPeriod,
                    AutoReplenishment = true
                });
            });

            options.AddPolicy("tenant-query", context =>
            {
                var partitionKey = context.GetTenantId() ??
                                   context.Connection.RemoteIpAddress?.ToString() ??
                                   "unknown";

                return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = opts.QueryPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 4,
                    AutoReplenishment = true
                });
            });
        });

        return services;
    }
}
