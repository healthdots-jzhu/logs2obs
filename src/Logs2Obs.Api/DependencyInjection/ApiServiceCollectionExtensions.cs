using System.Net;
using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
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

        // Trust X-Forwarded-For from ALB. ALB is in private subnets (10.0.0.0/8).
        // KnownNetworks covers all RFC1918 private ranges for flexibility.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            // Trust any RFC1918 address (covers ALB in 10.0.x.x private subnets)
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
        });

        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(5) };
            options.AddPolicy("IngestTimeout", TimeSpan.FromSeconds(10));
            options.AddPolicy("QueryTimeout", TimeSpan.FromSeconds(30));
        });

        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
            options.Preload = false; // Don't preload until domain is stable in production
        });

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