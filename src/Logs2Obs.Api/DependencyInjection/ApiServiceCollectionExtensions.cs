using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;

namespace Logs2Obs.Api.DependencyInjection;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddLogs2ObsApi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PayloadSizeOptions>(config.GetSection("PayloadSize"));
        services.Configure<RateLimiterOptions>(config.GetSection("RateLimiter"));
        services.Configure<OtelOptions>(config.GetSection("OpenTelemetry"));

        services.AddMemoryCache();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = ApiKeyAuthOptions.SchemeName;
            options.DefaultChallengeScheme = ApiKeyAuthOptions.SchemeName;
        })
        .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(
            ApiKeyAuthOptions.SchemeName,
            options => config.GetSection("ApiKey").Bind(options))
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            config.GetSection("Jwt").Bind(options);
        });

        services.AddAuthorization();

        return services;
    }
}
