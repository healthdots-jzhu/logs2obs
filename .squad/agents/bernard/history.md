# Project Context

- **Owner:** Jason Zhu
- **Project:** logs2obs / LightScope — Lightweight Observability & Log Intelligence Service
- **Stack:** .NET 10, C# 14, ASP.NET Core Minimal APIs + gRPC, MediatR 12, FluentValidation 11, Polly 8, Serilog, OpenTelemetry, Parquet.Net, S3/MinIO, OpenSearch, DynamoDB/PostgreSQL, SNS/SQS/RabbitMQ, Athena/DuckDB, AWS CDK (C#), Docker Compose, xUnit + Testcontainers + FluentAssertions
- **Design doc:** `.squad/docs/LightScope_Design_v3.md` (v3.0, 163KB)
- **Created:** 2026-03-24

## Key Architecture Facts

- Multi-service: LightScope.Core, LightScope.Api, LightScope.Worker, LightScope.Puller, LightScope.QueryEngine, LightScope.Adapters.{Local,Aws,Azure,Gcp,Kafka}, infra/cdk
- 14 implementation phases — Phase 1 (Core) through Phase 14 (Docs)
- All cloud integrations behind interfaces (IObjectStore, IMessageBus, ISearchIndexer, IQueryEngine, IMetadataStore, ISchemaRegistry, IIdempotencyStore)
- Local adapter first rule: no cloud adapter ships without local equivalent
- LightScope.Core must have zero cloud SDK references (enforced via Directory.Build.props)
- SNS fanout → 8 SQS queues + 8 DLQs; 2 SNS topics total (18 messaging resources)
- Query tier routing: Hot (OpenSearch, <3 days), Warm (Athena/DuckDB, 3–90 days), Cold (Athena+Glacier, >90 days), CrossTier (fan-out)
- Multi-tenant: TenantId always from auth context, never from DTO
- Exactly-once via Redis idempotency store (check before processing)
- AI NL→SQL via GitHub Models API (openai/gpt-4o) with ISqlSafetyValidator + audit log

### Phase 2 — Logs2Obs.Adapters.Local (2026-03-24)

**Commit:** TBD (Phase 2+3 commit)

**Build Status:** SUCCESS (0 errors, 78 warnings)

**Files Created (src/Logs2Obs.Adapters.Local/):**
- 10 adapter implementations: MinioObjectStore, RabbitMqMessageBus, InProcessChannelMessageBus, PostgresMetadataStore, PostgresSchemaRegistry, MeilisearchIndexer, DuckDbQueryEngine, RedisIdempotencyStore, RedisMatViewEngine, LocalSecretStore, QuartzScheduler, OllamaAiService
- Options: MinioOptions, RabbitMqOptions, PostgresOptions, RedisOptions, MeilisearchOptions, DuckDbOptions, OllamaOptions
- DI extension: LocalAdaptersServiceCollectionExtensions
- README.md for adapter documentation

**Files Created (tests/Logs2Obs.Adapters.Local.Tests/):**
- 6 integration test classes with Testcontainers
- 4 container fixtures (Minio, PostgreSql, RabbitMq, Redis)
- xUnit collection definitions

**Files Created (docker/):**
- docker-compose.yml with all local services
- 4 Dockerfiles
- init-scripts/01-schema.sql

**Key Fixes Made During Phase 2 Commit:**
- Adapter constructors refactored: `MinioObjectStore` and `RedisIdempotencyStore` create their own connections internally (removed `IMinioClient` and `IConnectionMultiplexer` constructor params) — allows `new(Options.Create(...))` pattern in tests
- `InProcessChannelMessageBus`, `PostgresMetadataStore`: made logger optional (default: NullLogger)
- Test project: added `GlobalUsings.cs` with `global using Xunit;` to resolve xunit attributes globally
- Test project: added `TreatWarningsAsErrors=false` (CA1707 underscores in test method names, CA1711 'Collection' suffix — both valid test conventions)
- `Options.Create()` namespace ambiguity: resolved by placing `using Options = Microsoft.Extensions.Options.Options;` INSIDE the file-scoped namespace body (after the namespace declaration), NOT before it — critical: outer namespace hierarchy lookup takes precedence over compilation-unit-level using aliases

**C# Quirk Documented:**
In a file-scoped namespace `namespace A.B.C.Tests.Foo;`, identifiers are resolved via outer namespace hierarchy BEFORE checking compilation-unit-level using aliases. To override `A.B.C.Options` namespace resolution when using `Options.Create(...)`, place `using Options = Microsoft.Extensions.Options.Options;` AFTER the `namespace` declaration (inside the namespace body), not before it.



### Phase 1 — LightScope.Core Scaffold (2026-03-24)

**Files Created:**
- `global.json` — .NET 10 SDK pin
- `Directory.Build.props` — cloud SDK reference enforcement
- `LightScope.slnx` — solution file (.NET 10 uses .slnx format, not .sln)
- `src/LightScope.Core/LightScope.Core.csproj` — net10.0, nullable, implicit usings
- `src/LightScope.Core/Models/` — 10 enums, 12 domain/DTO records
- `src/LightScope.Core/Exceptions/LightScopeExceptions.cs` — 9-class exception hierarchy
- `src/LightScope.Core/Abstractions/` — 14 interfaces + 7 result/value types
- `src/LightScope.Core/Schema/` — SchemaField, SchemaVersion, SchemaInferenceEngine
- `src/LightScope.Core/Mapping/DtoMapper.cs` — DTO→Domain with UUIDv7, TenantId guard
- `src/LightScope.Core/Validation/LogEntryDtoValidator.cs` — FluentValidation (Section 27.9)
- `src/LightScope.Core/Query/` — SqlSafetyValidator, QueryTierRouter (6 rules), TenantQueryInjector, ParsedQuery, QueryTierDecision, SubQuery
- `src/LightScope.Core/Storage/S3PathBuilder.cs`
- `src/LightScope.Core/Resilience/ResiliencePipelines.cs` — Polly 8 (ForExternalIo, ForSearch, ForStorage)
- `src/LightScope.Core/Commands/` — 4 commands + 1 result type
- `src/LightScope.Core/Handlers/` — 4 stub handlers
- `src/LightScope.Core/Graphs/` — ColumnInfo, QueryResultSchema, GraphSuggestion, GraphSuggestionEngine
- `src/LightScope.Core/MatViews/` — MatViewDefinition, StandardMatViews (3 views with exact SQL)
- `src/LightScope.Core/AI/AiQueryAudit.cs`

**Build Result:** SUCCESS (net10.0, 0 errors)

**Key Architectural Decisions:**
- `LogLevel` enum named `LightScope.Core.Models.LogLevel` — conflicts with Microsoft.Extensions.Logging.LogLevel resolved via `<Using Remove="Microsoft.Extensions.Logging" />` in csproj
- `IMessageBus.PublishAsync<T>` uses simple signature (no MessageAttributes) — matches task spec; attributes added in Phase 4 if needed
- `IMetadataStore.QueryAsync` uses `Func<T,bool>` filter per task spec (in-memory filtering) — adapters may optimize this
- `IIdempotencyStore` uses `ValueTask` for hot-path CheckAndSetAsync as specified (avoids allocation on fast path)
- `PendingQueryConfirmation` is a class (mutable) as specified, not a record
- Microsoft.Extensions.* packages use 9.* (not 10.*) since .NET 10 NuGet packages may not be released; rollForward handles this
- All handlers are stubs with TODO Phase N comments — implementation deferred to respective phases
- .NET 10 `dotnet new sln` creates `.slnx` format (not `.sln`) — use `logs2obs.slnx` for all sln commands

### Rename: LightScope → logs2obs/Logs2Obs (2026-03-24)

**Rename Complete.** All Phase 1 artifacts renamed from LightScope to logs2obs/Logs2Obs.

**Files renamed:**
- `LightScope.slnx` → `logs2obs.slnx`
- `src/LightScope.Core/` (directory) → `src/Logs2Obs.Core/`
- `src/Logs2Obs.Core/LightScope.Core.csproj` → `src/Logs2Obs.Core/Logs2Obs.Core.csproj`
- `src/Logs2Obs.Core/Exceptions/LightScopeExceptions.cs` → `src/Logs2Obs.Core/Exceptions/Logs2ObsExceptions.cs`
- `tests/LightScope.Core.Tests/` (directory) → `tests/Logs2Obs.Core.Tests/`
- `tests/Logs2Obs.Core.Tests/LightScope.Core.Tests.csproj` → `tests/Logs2Obs.Core.Tests/Logs2Obs.Core.Tests.csproj`

**Content updated:** All .cs namespaces (`LightScope.Core.*` → `Logs2Obs.Core.*`), all `using` statements, csproj properties (`<RootNamespace>`, `<AssemblyName>`), Directory.Build.props MSBuildProjectName conditions, solution project path and name.

**Class rename:** `LightScopeException` (base class in exceptions hierarchy) → `Logs2ObsException`

**Final namespace prefix:** `Logs2Obs` (PascalCase, valid C# identifier)

**Build Result after rename:** SUCCESS (`Logs2Obs.Core net10.0` → `Logs2Obs.Core.dll`, 0 errors)

## Learnings

- Phase 2+3 commit status: committed. Build result: succeeded. Commit hash: 317a3be2afececc4218df3ad9d3c081bcb064ec4.

### Multi-IdP JWT Auth (feat/multi-idp-auth)

- **Partial implementation found:** When this task started, stub files for `IdentityProviderOptions`, `ClaimsNormalizationMiddleware`, and `MultiIdpAuthExtensions` already existed but were incomplete/incorrect. Always grep for existing partial work before creating new files.
- **`AuthOptions` → `MultiIdpOptions` rename:** The stub used `AuthOptions` (with `SectionName = "Auth"` const) as the config wrapper. The spec uses `MultiIdpOptions`. Keeping the name consistent with the spec avoids confusion; `SectionName` constants are handy but not required when the section path is spelled out at the call site.
- **ClaimsMappings direction:** The stub had the dictionary interpretation backwards (Key=canonical, Value=IdP). The correct direction is Key=IdP-specific claim name, Value=canonical claim name. The `ClaimsNormalizationMiddleware` iterates `(sourceName, targetName)` from config.
- **`TokenValidationParameters.AuthenticationType`:** Must be explicitly set to `idp.Name` when registering each `AddJwtBearer` scheme. Without this, all JWT identities get `AuthenticationType = "Bearer"` (the default), making scheme-name matching in `ClaimsNormalizationMiddleware` impossible.
- **`ClaimsNormalizationMiddleware` design:** Appends a new `ClaimsIdentity` with mapped claims rather than mutating the existing identity. `ClaimsPrincipal.FindFirst()` searches all identities, so the canonical `tenantId` claim is found by `TenantContextMiddleware` regardless of which identity it belongs to.
- **Backward compat guard:** `idps.Length > 0` check preserves the legacy single `Jwt` config-section path. When no `Auth:IdentityProviders` are configured, the old `JwtBearerDefaults.AuthenticationScheme` scheme is registered.
- **`ILogger<T>` removed from middleware:** The csproj has `<Using Remove="Microsoft.Extensions.Logging" />` — using `ILogger<T>` requires an explicit `using` directive and risks the `LogLevel` ambiguity with `Logs2Obs.Core.Models.LogLevel`. For a pass-through middleware with no I/O, logging adds no value.
- **Build result:** `dotnet build logs2obs.slnx --configuration Release` → SUCCESS. All 221 non-adapter-local tests passed.

### Multi-IdP Authentication (2026-04-01)

**Commit:** 625362b  
**Build Status:** SUCCESS (0 errors, 0 warnings)  
**Tests:** 214 passed, 4 skipped (pre-existing JwtAuthTests stubs), 0 failed

**Files Created:**
- `src/Logs2Obs.Api/Auth/IdentityProviderOptions.cs` — `AuthOptions` (SectionName="Auth") + `IdentityProviderOptions` (required Name, Authority, Audiences, ClaimsMappings)
- `src/Logs2Obs.Api/Auth/ClaimsNormalizationMiddleware.cs` — post-auth claim rewriting per IdP's ClaimsMappings; `UseClaimsNormalization()` extension
- `src/Logs2Obs.Api/Extensions/MultiIdpAuthExtensions.cs` — `AddMultiIdpAuthentication()` loops IdPs, calls `AddJwtBearer(idp.Name,...)`, builds default AuthorizationPolicy accepting all schemes
- `src/Logs2Obs.Api/appsettings.json` — base config with Cognito + EntraID `{REPLACE_ME}` placeholders
- `src/Logs2Obs.Api/appsettings.Development.json` — empty IdentityProviders list for dev ApiKey-only mode

**Files Modified:**
- `ApiServiceCollectionExtensions.cs` — replaced single-IdP JWT setup with `AddMultiIdpAuthentication(config)`
- `Program.cs` — added `app.UseClaimsNormalization()` after `app.UseAuthentication()`
- `TenantContextMiddleware.cs` — checks `tenant_id` (JWT canonical) first, falls back to `tenantId` (ApiKey legacy)

**Key Design Points:**
- OIDC discovery from Authority URI — no secrets in config, RS256 public keys auto-fetched from jwks_uri
- `string[] Audiences` supports multiple client IDs per Cognito pool
- `ClaimsMappings` is key=canonical, value=IdP-specific (e.g. `"tenant_id": "custom:tenantId"`)
- ApiKeyAuthHandler unchanged — still emits "tenantId"; TenantContextMiddleware handles both names
- Zero Trust: M2M internal services use their own Cognito IdP entry

**Skill documented:** `.squad/skills/multi-idp-auth/SKILL.md`  
**Decision documented:** `.squad/decisions/inbox/bernard-multi-idp-auth.md`

### Fix: RateLimiterOptions Config Wiring + API Security Hardening Review (2026-04-01)

**Commit:** 3bc9cfe  
**Build Status:** SUCCESS (0 errors, 0 warnings)  
**Tests:** 221 passed, 25 skipped (AWS + JWT stubs), 0 failed

**Problem:** `TenantRateLimiterExtensions.cs` hardcoded `TokenLimit = 1000`, `TokensPerPeriod = 500`, `PermitLimit = 100` — identical to the defaults in `RateLimiterOptions`, meaning `appsettings.json`'s `RateLimiter` section was silently ignored.

**Fix:**
- `AddTenantRateLimiting()` now accepts `IConfiguration config` parameter.
- Reads `config.GetSection("RateLimiter").Get<ApiRateLimiterOptions>()` at startup, falls back to `new ApiRateLimiterOptions()` if section is absent.
- `Program.cs` updated from `AddTenantRateLimiting()` → `AddTenantRateLimiting(builder.Configuration)`.

**Name collision quirk:** `Microsoft.AspNetCore.RateLimiting` also has a `RateLimiterOptions` type. Resolved with a using alias: `using ApiRateLimiterOptions = Logs2Obs.Api.Options.RateLimiterOptions;` — keeps the code unambiguous without renaming the options class.

**Files Modified:**
- `src/Logs2Obs.Api/RateLimiting/TenantRateLimiterExtensions.cs` — accepts `IConfiguration`, reads opts from config section
- `src/Logs2Obs.Api/Program.cs` — passes `builder.Configuration` to `AddTenantRateLimiting`

**Security Hardening Review:** Produced a full DDoS / API hardening table for Jason covering 12 concern areas. Key gaps identified:
1. WAF has no rate-based rule — highest priority to add
2. `UseForwardedHeaders` missing — anonymous bucket always "unknown" behind ALB
3. No Kestrel min data rate — slow loris exposure
4. No request timeouts — thread exhaustion risk
5. No security response headers — X-Content-Type-Options, X-Frame-Options etc. absent
6. HSTS not configured in app layer (ALB redirects HTTP, but no HSTS header sent)

**Already solid:** tenant rate limiting (now config-driven), payload size limit, multi-IdP JWT, WAF managed rules, HTTP→HTTPS ALB redirect, OTel + Serilog, Polly circuit breakers.

**Decision documented:** `.squad/decisions/inbox/bernard-api-hardening.md`

### Security Hardening Implementation: ForwardedHeaders, Kestrel Limits, Request Timeouts (2026-04-01)

**Commit:** 29d5df5  
**Build Status:** SUCCESS (0 errors, 0 warnings)  
**Tests:** 221 passed, 25 skipped, 0 failed

**Problem:** Three critical ASP.NET Core security hardening items missing:
1. `UseForwardedHeaders` — anonymous rate limiter buckets all ALB traffic to "unknown" IP
2. Kestrel min data rate — slow loris / connection exhaustion vulnerability
3. Request timeouts — thread exhaustion risk on long-running queries

**Changes Made:**

**Item 1: ForwardedHeaders (fixes anonymous IP bucketing)**
- `ApiServiceCollectionExtensions.cs`: Added `ForwardedHeadersOptions` configuration trusting X-Forwarded-For from RFC1918 private subnets (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16) to cover ALB in 10.0.x.x VPC subnets.
- `Program.cs`: Added `app.UseForwardedHeaders()` as FIRST middleware (before `UseSerilogRequestLogging()`), ensuring real client IP flows to rate limiter, Serilog, and all downstream middleware.
- Used `KnownIPNetworks` (not obsolete `KnownNetworks`) and fully-qualified `System.Net.IPNetwork` to avoid ambiguity with `Microsoft.AspNetCore.HttpOverrides.IPNetwork`.

**Item 2: Kestrel Min Data Rate (slow loris mitigation)**
- `Program.cs`: Added `ConfigureKestrel()` with `MinRequestBodyDataRate` and `MinResponseDataRate` (100 bytes/sec, 10s grace), `RequestHeadersTimeout` (15s), `KeepAliveTimeout` (120s).

**Item 3: Request Timeouts (per-endpoint timeout policies)**
- `ApiServiceCollectionExtensions.cs`: Added `AddRequestTimeouts()` with 5s default, 10s "IngestTimeout", 30s "QueryTimeout".
- `Program.cs`: Added `app.UseRequestTimeouts()` after `UseExceptionHandler()` and before `PayloadSizeMiddleware`.
- `LogsEndpoints.cs`: Applied `.WithRequestTimeout("IngestTimeout")` to all 3 ingest endpoints.
- `QueryEndpoints.cs`: Applied `.WithRequestTimeout("QueryTimeout")` to all 8 query endpoints.
- Used fully-qualified `Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy` in DI config to avoid needing using directive.

**Key Quirks:**
- .NET 10 obsoleted `ForwardedHeadersOptions.KnownNetworks` → `KnownIPNetworks` (ASPDEP PR005 warnings treated as errors).
- `IPNetwork` ambiguity: both `System.Net.IPNetwork` and `Microsoft.AspNetCore.HttpOverrides.IPNetwork` exist; used fully-qualified `System.Net.IPNetwork`.
- `RequestTimeoutPolicy` required `Microsoft.AspNetCore.Http.Timeouts` using in endpoints files; fully-qualified in DI config to avoid polluting global usings.

**Security impact:** Rate limiter now sees real client IPs (not ALB IP), slow clients are rejected within 10s, queries time out at 30s max (prevents thread pool exhaustion).

**Decision documented:** `.squad/decisions/inbox/bernard-security-hardening.md`

### 2026-04-01: API Security Hardening — ForwardedHeaders, Kestrel, Request Timeouts

**Commit:** 29d5df5

**Implemented:**
1. **UseForwardedHeaders middleware** — Reads X-Forwarded-For from AWS ALB, trusts RFC1918 CIDR ranges (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16). Fixes rate limiter IP bucketing for anonymous traffic.
2. **Kestrel minimum data rate limits** — Set MinRequestBodyDataRate to 100 bytes/sec with 10s grace period + RequestHeadersTimeout 15s. Defends against slow loris attacks.
3. **Per-endpoint request timeouts** — Added 10s for ingest, 30s for query, 5s default to prevent thread pool exhaustion.

**Build & Tests:**
- Build: 0 errors, 0 warnings ✅
- Tests: 221 passed, 25 skipped, 0 failed ✅

**Files modified:**
- src/Logs2Obs.Api/DependencyInjection/ApiServiceCollectionExtensions.cs
- src/Logs2Obs.Api/Program.cs
- src/Logs2Obs.Api/Endpoints/LogsEndpoints.cs
- src/Logs2Obs.Api/Endpoints/QueryEndpoints.cs

**Key decision:** Middleware order is critical — UseForwardedHeaders() must be first (before Serilog + rate limiter) so all downstream middleware sees the real client IP.

**Next monitoring:** Verify real client IPs in ALB access logs and CloudWatch Insights rate limiter metrics.
