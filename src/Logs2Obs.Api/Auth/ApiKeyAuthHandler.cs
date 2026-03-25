using System.Security.Claims;
using System.Text.Encodings.Web;
using Logs2Obs.Core.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logs2Obs.Api.Auth;

public sealed class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    private readonly IMemoryCache _cache;
    private readonly IMetadataStore _metadataStore;
    private readonly ILogger<ApiKeyAuthHandler> _logger;

    public ApiKeyAuthHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IMemoryCache cache,
        IMetadataStore metadataStore,
        ILogger<ApiKeyAuthHandler> logger)
        : base(options, loggerFactory, encoder)
    {
        _cache = cache;
        _metadataStore = metadataStore;
        _logger = logger;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var apiKeyValues))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = apiKeyValues.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AuthenticateResult.Fail("Invalid API key format");
        }

        var cacheKey = $"apikey:{apiKey}";
        
        if (_cache.TryGetValue<(string TenantId, string KeyId)>(cacheKey, out var cached))
        {
            var claims = new[]
            {
                new Claim("tenantId", cached.TenantId),
                new Claim("sub", cached.KeyId),
                new Claim(ClaimTypes.Name, cached.KeyId)
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return AuthenticateResult.Success(ticket);
        }

        var metadata = await _metadataStore.GetAsync<Dictionary<string, string>>("api_keys", apiKey, Context.RequestAborted);
        if (metadata == null)
        {
            _logger.LogWarning("API key not found: {ApiKey}", apiKey);
            return AuthenticateResult.Fail("Invalid API key");
        }

        if (!metadata.TryGetValue("tenantId", out var tenantId) || string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("API key missing tenantId: {ApiKey}", apiKey);
            return AuthenticateResult.Fail("Invalid API key configuration");
        }

        if (metadata.TryGetValue("active", out var activeStr) && bool.TryParse(activeStr, out var active) && !active)
        {
            _logger.LogWarning("API key inactive: {ApiKey}", apiKey);
            return AuthenticateResult.Fail("API key is inactive");
        }

        var keyId = metadata.TryGetValue("keyId", out var kid) ? kid : apiKey[..Math.Min(8, apiKey.Length)];

        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Options.CacheDurationSeconds)
        };
        _cache.Set(cacheKey, (tenantId, keyId), cacheOptions);

        var validClaims = new[]
        {
            new Claim("tenantId", tenantId),
            new Claim("sub", keyId),
            new Claim(ClaimTypes.Name, keyId)
        };
        var validIdentity = new ClaimsIdentity(validClaims, Scheme.Name);
        var validPrincipal = new ClaimsPrincipal(validIdentity);
        var validTicket = new AuthenticationTicket(validPrincipal, Scheme.Name);

        _logger.LogInformation("API key authenticated: TenantId={TenantId}, KeyId={KeyId}", tenantId, keyId);
        return AuthenticateResult.Success(validTicket);
    }
}
