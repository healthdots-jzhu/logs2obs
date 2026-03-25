using System.Threading.RateLimiting;
using Logs2Obs.Api.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace Logs2Obs.Api.RateLimiting;

public static class TenantRateLimiterExtensions
{
    public static IServiceCollection AddTenantRateLimiting(this IServiceCollection services)
    {
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
                    TokenLimit = 1000,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    TokensPerPeriod = 500,
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
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 4,
                    AutoReplenishment = true
                });
            });
        });

        return services;
    }
}
