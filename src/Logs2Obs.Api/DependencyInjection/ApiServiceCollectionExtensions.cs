using Logs2Obs.Api.Extensions;
using Logs2Obs.Api.Options;

namespace Logs2Obs.Api.DependencyInjection;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddLogs2ObsApi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PayloadSizeOptions>(config.GetSection("PayloadSize"));
        services.Configure<RateLimiterOptions>(config.GetSection("RateLimiter"));
        services.Configure<OtelOptions>(config.GetSection("OpenTelemetry"));

        services.AddMemoryCache();

        services.AddMultiIdpAuthentication(config);

        return services;
    }
}