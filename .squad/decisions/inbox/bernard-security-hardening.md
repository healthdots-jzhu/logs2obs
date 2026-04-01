# Security Hardening: ForwardedHeaders, Kestrel Limits, Request Timeouts

**Date:** 2026-04-01  
**Author:** Bernard (Lead & Architect)  
**Commit:** 29d5df5  
**Status:** ✅ Implemented

---

## Context

Three ASP.NET Core security hardening items were missing from `Logs2Obs.Api`:

1. **UseForwardedHeaders** — Rate limiter falls back to `RemoteIpAddress` for anonymous/unauthenticated requests, but when behind AWS ALB, ALL traffic arrives from ALB's IP → everything buckets to "unknown"
2. **Kestrel minimum data rate** — No protection against slow loris / connection exhaustion attacks
3. **Request timeouts** — No per-endpoint timeout policies → risk of thread pool exhaustion on long-running queries

---

## Decision

Implemented all three hardening items:

### 1. ForwardedHeaders (Fix Anonymous IP Bucketing)

**Configuration (`ApiServiceCollectionExtensions.cs`):**
```csharp
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
```

**Middleware (`Program.cs`):**
```csharp
app.UseForwardedHeaders();  // FIRST middleware, before UseSerilogRequestLogging()
```

**Rationale:**
- ALB is in private subnets (10.0.x.x). Trusting all RFC1918 ranges provides flexibility for future infra changes.
- Must be FIRST middleware so Serilog, rate limiter, and all downstream middleware see the real client IP.

**Key Quirks:**
- .NET 10 obsoleted `ForwardedHeadersOptions.KnownNetworks` → `KnownIPNetworks` (ASPDEP PR005 warnings treated as errors).
- `IPNetwork` ambiguity: both `System.Net.IPNetwork` and `Microsoft.AspNetCore.HttpOverrides.IPNetwork` exist → used fully-qualified `System.Net.IPNetwork`.

---

### 2. Kestrel Minimum Data Rate (Slow Loris Mitigation)

**Configuration (`Program.cs`):**
```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MinRequestBodyDataRate = new MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(10));
    options.Limits.MinResponseDataRate = new MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(10));
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(120);
});
```

**Rationale:**
- 100 bytes/sec minimum prevents slow loris attacks from exhausting connections.
- 10s grace period allows for legitimate slow starts (e.g., mobile networks).
- 15s header timeout prevents header-based attacks.
- 120s keep-alive balances connection reuse vs. resource consumption.

---

### 3. Request Timeouts (Per-Endpoint Timeout Policies)

**Configuration (`ApiServiceCollectionExtensions.cs`):**
```csharp
services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy 
        { Timeout = TimeSpan.FromSeconds(5) };
    options.AddPolicy("IngestTimeout", TimeSpan.FromSeconds(10));
    options.AddPolicy("QueryTimeout", TimeSpan.FromSeconds(30));
});
```

**Middleware (`Program.cs`):**
```csharp
app.UseRequestTimeouts();  // After UseExceptionHandler(), before PayloadSizeMiddleware
```

**Endpoint Decoration:**
- `LogsEndpoints.cs`: All 3 ingest endpoints → `.WithRequestTimeout("IngestTimeout")` (10s)
- `QueryEndpoints.cs`: All 8 query endpoints → `.WithRequestTimeout("QueryTimeout")` (30s)
- Other endpoints: Default 5s policy (automatic)

**Rationale:**
- Ingest endpoints (POST logs/bulk/metrics) need 10s for large payloads.
- Query endpoints may execute complex SQL → 30s.
- 5s default protects all other endpoints (health, auth, etc.).
- Prevents thread pool exhaustion from runaway requests.

**Key Quirks:**
- `RequestTimeoutPolicy` required `Microsoft.AspNetCore.Http.Timeouts` using in endpoint files.
- Used fully-qualified type in DI config to avoid polluting global usings.

---

## Consequences

**Positive:**
- ✅ Rate limiter now sees real client IPs (not ALB IP) → proper per-IP throttling for anonymous traffic.
- ✅ Slow loris attacks rejected within 10s → connection exhaustion protection.
- ✅ All endpoints have bounded execution time → thread pool exhaustion protection.
- ✅ Query timeouts prevent runaway queries from blocking workers.

**Neutral:**
- `TreatWarningsAsErrors=true` caught two obsolete API usages → forced to use current .NET 10 APIs.
- Middleware order now critical: `UseForwardedHeaders()` must be first, `UseRequestTimeouts()` must be before business logic.

**Risks:**
- If ALB moves to public subnets or a different CIDR, `KnownIPNetworks` must be updated.
- 30s query timeout may be too short for very complex queries → monitor and adjust if needed.
- 100 bytes/sec min data rate may reject legitimate slow connections (e.g., satellite) → monitor and adjust if needed.

---

## Files Changed

1. `src/Logs2Obs.Api/DependencyInjection/ApiServiceCollectionExtensions.cs`
   - Added `ForwardedHeadersOptions` configuration (RFC1918 trust)
   - Added `AddRequestTimeouts()` with 3 policies
   - Added usings: `System.Net`, `Microsoft.AspNetCore.HttpOverrides`

2. `src/Logs2Obs.Api/Program.cs`
   - Added `ConfigureKestrel()` with min data rate limits
   - Added `app.UseForwardedHeaders()` (FIRST middleware)
   - Added `app.UseRequestTimeouts()` (after exception handler)
   - Added using: `Microsoft.AspNetCore.Server.Kestrel.Core`

3. `src/Logs2Obs.Api/Endpoints/LogsEndpoints.cs`
   - Applied `.WithRequestTimeout("IngestTimeout")` to 3 endpoints
   - Added using: `Microsoft.AspNetCore.Http.Timeouts`

4. `src/Logs2Obs.Api/Endpoints/QueryEndpoints.cs`
   - Applied `.WithRequestTimeout("QueryTimeout")` to 8 endpoints
   - Added using: `Microsoft.AspNetCore.Http.Timeouts`

---

## Testing

- **Build:** `dotnet build --configuration Release` → SUCCESS (0 errors, 0 warnings)
- **Tests:** `dotnet test --configuration Release --filter "FullyQualifiedName!~Adapters.Local"` → 221 passed, 25 skipped, 0 failed

---

## Next Steps

1. **Monitor ALB access logs** for X-Forwarded-For behavior → confirm real client IPs are being logged.
2. **Monitor CloudWatch Insights** for rate limiter buckets → confirm per-IP throttling works for anonymous traffic.
3. **Monitor request timeout metrics** → adjust 30s query timeout if legitimate queries are timing out.
4. **Consider adding** `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` response headers (item #5 from hardening review).
5. **Consider adding** HSTS header with `max-age=31536000; includeSubDomains` (item #6 from hardening review).
