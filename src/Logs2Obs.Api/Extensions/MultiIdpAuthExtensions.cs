using Logs2Obs.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Logs2Obs.Api.Extensions;

public static class MultiIdpAuthExtensions
{
    public static IServiceCollection AddMultiIdpAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authOptions = configuration
            .GetSection(AuthOptions.SectionName)
            .Get<AuthOptions>() ?? new AuthOptions();

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));

        var idps = authOptions.IdentityProviders;

        var authBuilder = services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ApiKeyAuthOptions.SchemeName;
                options.DefaultChallengeScheme = ApiKeyAuthOptions.SchemeName;
            })
            .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(
                ApiKeyAuthOptions.SchemeName,
                options => configuration.GetSection("ApiKey").Bind(options));

        foreach (var idp in idps)
        {
            var capturedIdp = idp;
            authBuilder.AddJwtBearer(capturedIdp.Name, options =>
            {
                options.Authority = capturedIdp.Authority;
                options.TokenValidationParameters.ValidAudiences = capturedIdp.Audiences;
            });
        }

        var allSchemes = new[] { ApiKeyAuthOptions.SchemeName }
            .Concat(idps.Select(p => p.Name))
            .ToArray();

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(allSchemes)
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
