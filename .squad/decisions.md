# Squad Decisions

## Active Decisions

---

### 2026-03-24: Product Identity — logs2obs Naming Convention

**By:** Bernard (Lead & Architect), confirmed by Jason Zhu  
**User directive:** 2026-03-24T14-24-53 — "The product name is **logs2obs** (not LightScope)."

| Context | Convention | Example |
|---|---|---|
| Product name (human-facing, filenames, slugs) | `logs2obs` (all lowercase) | `logs2obs.slnx`, README title |
| C# namespace prefix | `Logs2Obs` (PascalCase) | `namespace Logs2Obs.Core.Models` |
| Assembly names | `Logs2Obs.<Component>` | `Logs2Obs.Core`, `Logs2Obs.Api` |
| Project/csproj file names | `Logs2Obs.<Component>.csproj` | `Logs2Obs.Core.csproj` |
| Solution file name | `logs2obs.slnx` (lowercase) | `logs2obs.slnx` |
| Source directories | `Logs2Obs.<Component>` | `src/Logs2Obs.Core/` |
| Test project names | `Logs2Obs.<Component>.Tests` | `Logs2Obs.Core.Tests` |
| Base exception class | `Logs2ObsException` | `public abstract class Logs2ObsException` |

**Rationale:** `logs2obs` is the official product identifier. PascalCase `Logs2Obs` is required for valid C# namespace/identifier syntax. The digit `2` is preserved in all forms.

**Supersedes:** "LightScope" — all references replaced in Phase 1 rename task (2026-03-24). No new code may use "LightScope".

---

### 2026-03-24: Phase 1 Architectural Decisions (Logs2Obs.Core)

**By:** Bernard (Lead & Architect)

#### Decision 1: Namespace Strategy
Use `Logs2Obs.Core.{SubFolder}` for all namespaces. Matches physical folder structure exactly. Team members can infer namespace from folder path; no aliasing needed.

#### Decision 2: Records for All Domain Models
All domain models (`LogEntry`, `TenantSettings`, `ReplayJob`, etc.) are `sealed record` with `required` + `init` properties. Immutability prevents accidental mutation; value equality is correct for domain objects; `required` provides compile-time field population safety. Handlers cannot modify domain objects after creation — all "updates" produce new instances via `with` expressions.

#### Decision 3: IIdempotencyStore Uses ValueTask
`CheckAndSetAsync` and `ExpireAsync` return `ValueTask` (not `Task`). At ~16,667 entries/sec, `ValueTask` avoids heap allocation when operations complete synchronously (e.g., Redis cache hit). Redis adapter can use `ValueTask.FromResult()` on cache hit.

#### Decision 4: IMessageBus Simple Signature
`PublishAsync<T>` takes `(string topic, T message, CancellationToken ct)` without `MessageAttributes`. SNS filter policies are an infrastructure concern — the SNS adapter derives them from message type via reflection or convention, not passed from Core.

#### Decision 5: LogLevel Namespace Conflict Resolution
Remove implicit `using Microsoft.Extensions.Logging;` via `<Using Remove="Microsoft.Extensions.Logging" />` in the csproj. Files that need `ILogger` must add the explicit using themselves. We own `Logs2Obs.Core.Models.LogLevel`; the csproj-level removal is the cleanest solution.

#### Decision 6: Microsoft.Extensions.* Package Versions
Use `9.*` for `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`, and `Microsoft.Extensions.DependencyInjection.Abstractions`. .NET 10 preview packages may not be on NuGet; `9.*` is fully compatible with `net10.0` TFM. Upgrade to `10.*` when .NET 10 GA ships.

#### Decision 7: ISchemaRegistry Simplified Interface
Use `IReadOnlyList<SchemaField>` instead of `SchemaDefinition` in `RegisterSchemaAsync`. The task spec explicitly specifies this signature; `SchemaDefinition` can be introduced in Phase 9 when the full schema registry is implemented.

#### Decision 8: .NET 10 Solution Format
Solution file is `logs2obs.slnx` (not `.sln`). `dotnet new sln` in .NET 10 creates the new `.slnx` XML format by default. All `dotnet sln` commands and CI/CD pipelines must reference `logs2obs.slnx`.

**What Phase 2 needs to know:**
- `IObjectStore.ReadAsync` returns `Stream?` (nullable) — local adapter must handle non-existent keys
- `TenantQueryInjector` uses `{TENANT_FILTER}` placeholder — all prebuilt SQL templates must include it
- All handlers are stubs — Phase 4 (API) completes `IngestLogsHandler`; Phase 7 completes `ExecuteSqlQueryHandler`
- `ResiliencePipelines` are static factories — adapters should cache pipelines as fields
- `SchemaField.InferredType` is string (`"string"`, `"int64"`, `"double"`, `"bool"`, `"timestamp"`)

---

### 2026-03-24: Core API Surface Assumptions (Stubbs — Logs2Obs.Core.Tests)

**By:** Stubbs (QA & Test Engineer)  
**Context:** Tests scaffolded anticipatorily before Core was built. These assumptions may need validation when tests are first run.

**Confirmed by Bernard (Phase 1 completion):**
- `LogLevel` enum values: `Trace, Debug, Info, Warn, Error, Fatal` (NOT `Information`/`Warning`)
- `DtoMapper.ToDto(LogEntry domain)` reverse method exists (Bernard implemented it)
- `MetricDto` in `Logs2Obs.Core.Models` with `MetricName, Unit, Value, MetricType`; `MetricPayloadDto` also exists for `LogEntryDto.Metric`

**Open assumptions (to verify on first test run):**
1. `QueryResultSchema` constructor signature: `(IEnumerable<QueryColumn> columns, int rowCount)` — assumed, not explicit in design
2. `TenantSettings.IsActive: bool` — not mentioned in design §7.3; confirm Bernard included it
3. `SubQueries` is null/empty (not `[]`) for entirely-Cold queries

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
