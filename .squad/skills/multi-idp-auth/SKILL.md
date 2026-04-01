# Skill: Config-Driven Multi-IdP Authentication (ASP.NET Core)

**Author:** Bernard  
**Applies to:** Logs2Obs.Api, any ASP.NET Core 8+ Minimal API

---

## Pattern

Register N identity providers from config, each using OIDC discovery (no secrets). One `ClaimsNormalizationMiddleware` normalizes IdP-specific claim names to canonical names. Authorization policy accepts tokens from any registered scheme.

## Key Files (template)

### 1. Options model

```csharp
public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public List<IdentityProviderOptions> IdentityProviders { get; init; } = [];
}

public sealed class IdentityProviderOptions
{
    public required string Name { get; init; }
    public required string Authority { get; init; }        // OIDC issuer URI
    public required string[] Audiences { get; init; }     // accepted aud values
    public Dictionary<string, string> ClaimsMappings { get; init; } = []; // canonical → idp-specific
}
```

### 2. DI extension

```csharp
public static IServiceCollection AddMultiIdpAuthentication(
    this IServiceCollection services, IConfiguration configuration)
{
    var authOptions = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new();
    services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));

    var authBuilder = services.AddAuthentication(o => { /* set defaults */ });

    foreach (var idp in authOptions.IdentityProviders)
    {
        var captured = idp;
        authBuilder.AddJwtBearer(captured.Name, o =>
        {
            o.Authority = captured.Authority;
            o.TokenValidationParameters.ValidAudiences = captured.Audiences;
        });
    }

    var allSchemes = authOptions.IdentityProviders.Select(p => p.Name).ToArray();
    services.AddAuthorization(o =>
    {
        o.DefaultPolicy = new AuthorizationPolicyBuilder(allSchemes)
            .RequireAuthenticatedUser().Build();
    });
    return services;
}
```

### 3. Claims normalization middleware

```csharp
public sealed class ClaimsNormalizationMiddleware(
    RequestDelegate next, IOptions<AuthOptions> opts, ILogger<...> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated == true
            && ctx.User.Identity is ClaimsIdentity identity)
        {
            var idp = opts.Value.IdentityProviders
                .FirstOrDefault(p => p.Name == identity.AuthenticationType);
            if (idp != null)
                foreach (var (canonical, idpClaim) in idp.ClaimsMappings)
                {
                    var existing = identity.FindFirst(idpClaim);
                    if (existing != null && canonical != idpClaim)
                    {
                        identity.RemoveClaim(existing);
                        identity.AddClaim(new Claim(canonical, existing.Value));
                    }
                }
        }
        await next(ctx);
    }
}
```

### 4. Pipeline wiring

```csharp
app.UseAuthentication();
app.UseClaimsNormalization();   // must be AFTER UseAuthentication
app.UseAuthorization();
```

## Config shape

```json
"Auth": {
  "IdentityProviders": [
    {
      "Name": "Cognito-Internal",
      "Authority": "https://cognito-idp.us-east-1.amazonaws.com/{pool-id}",
      "Audiences": ["client-id-a"],
      "ClaimsMappings": { "tenant_id": "custom:tenantId" }
    }
  ]
}
```

## Rules

- **No secrets** — Authority triggers OIDC discovery; JWKS fetched automatically
- **Canonical claim names:** `tenant_id`, `sub`, `email`
- **Dev bypass:** Empty `IdentityProviders: []` falls back to ApiKey-only
- **Zero Trust:** M2M services get their own IdP entry (Cognito M2M); no subnet bypass
