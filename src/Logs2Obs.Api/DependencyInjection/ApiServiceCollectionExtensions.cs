using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace Logs2Obs.Api.DependencyInjection;

/// <summary>Extension methods for registering Logs2Obs API services.</summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Logs2Obs API services: options, caching, authentication (multi-IdP or legacy JWT),
    /// and authorization.
    /// </summary>
    public static IServiceCollection AddLogs2ObsApi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PayloadSizeOptions>(config.GetSection("PayloadSize"));
        services.Configure<RateLimiterOptions>(config.GetSection("RateLimiter"));
        services.Configure<OtelOptions>(config.GetSection("OpenTelemetry"));

        // Bind multi-IdP config so ClaimsNormalizationMiddleware can resolve IOptions<MultiIdpOptions>.
        services.Configure<MultiIdpOptions>(config.GetSection("Auth"));

        services.AddMemoryCache();

        var multiIdp = config.GetSection("Auth").Get<MultiIdpOptions>() ?? new MultiIdpOptions();
        var idps = multiIdp.IdentityProviders;

        var authBuilder = services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ApiKeyAuthOptions.SchemeName;
                options.DefaultChallengeScheme = ApiKeyAuthOptions.SchemeName;
            })
            .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(
                ApiKeyAuthOptions.SchemeName,
                options => config.GetSection("ApiKey").Bind(options));

        if (idps.Length > 0)
        {
            foreach (var idp in idps)
            {
                authBuilder.AddJwtBearer(idp.Name, options =>
                {
                    options.Authority = idp.Authority;
                    // Set AuthenticationType so ClaimsNormalizationMiddleware can match
                    // the scheme name against IdentityProviderOptions.Name.
                    options.TokenValidationParameters.AuthenticationType = idp.Name;

                    if (idp.Audiences.Length > 0)
                    {
                        options.TokenValidationParameters.ValidAudiences = idp.Audiences;
                        options.TokenValidationParameters.ValidateAudience = true;
                    }
                    else
                    {
                        options.TokenValidationParameters.ValidateAudience = false;
                    }
                });
            }

            var allSchemes = new[] { ApiKeyAuthOptions.SchemeName }
                .Concat(idps.Select(i => i.Name))
                .ToArray();

            services.AddAuthorization(opts =>
            {
                opts.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes(allSchemes)
                    .RequireAuthenticatedUser()
                    .Build();
                opts.FallbackPolicy = opts.DefaultPolicy;
            });
        }
        else
        {
            // Backward compat: no IdentityProviders configured — use single Jwt section.
            authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                config.GetSection("Jwt").Bind(options);
            });

            services.AddAuthorization();
        }

        return services;
    }
}