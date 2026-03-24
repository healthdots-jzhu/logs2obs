# Logs2Obs.Adapters.Local

**Local development adapters for the Logs2Obs observability platform.**

This project provides complete implementations of all 11 core infrastructure abstractions using local/open-source technologies. Designed for development, testing, and local prototyping — **NOT for production use**.

---

## 📦 What's Included

### Adapters Implemented

| Interface | Implementation | Technology | Purpose |
|-----------|---------------|------------|---------|
| `IObjectStore` | `MinioObjectStore` | MinIO | S3-compatible blob storage |
| `IMessageBus` | `RabbitMqMessageBus` | RabbitMQ | Pub/sub messaging |
| `IMessageBus` | `InProcessChannelMessageBus` | `System.Threading.Channels` | In-memory messaging (tests) |
| `IMetadataStore` | `PostgresMetadataStore` | PostgreSQL + Dapper | Configuration/state storage |
| `ISchemaRegistry` | `PostgresSchemaRegistry` | PostgreSQL | Tenant schema versioning |
| `ISearchIndexer` | `MeilisearchIndexer` | Meilisearch | Hot-tier log search |
| `IQueryEngine` | `DuckDbQueryEngine` | DuckDB | Embedded analytics engine |
| `IIdempotencyStore` | `RedisIdempotencyStore` | Redis | Duplicate detection |
| `IMatViewEngine` | `RedisMatViewEngine` | Redis | Materialized view cache |
| `ISecretStore` | `LocalSecretStore` | In-memory | Secret storage (dev only) |
| `IAiService` | `OllamaAiService` | Ollama | Natural language → SQL |
| `IScheduler` | `QuartzScheduler` | Quartz.NET | Cron-based job scheduling |

---

## 🚀 Quick Start

### 1. Install Dependencies

```bash
# Docker Compose recommended for local stack
docker compose -f infra/docker-compose.local.yml up -d
```

**Services Started:**
- MinIO (9000, 9001)
- RabbitMQ (5672, 15672)
- PostgreSQL (5432)
- Redis (6379)
- Meilisearch (7700)
- Ollama (11434)

### 2. Configure appsettings.json

```json
{
  "LocalAdapters": {
    "Minio": {
      "Endpoint": "localhost:9000",
      "AccessKey": "minioadmin",
      "SecretKey": "minioadmin",
      "BucketName": "logs2obs",
      "UseSSL": false
    },
    "RabbitMq": {
      "HostName": "localhost",
      "Port": 5672,
      "UserName": "guest",
      "Password": "guest",
      "VirtualHost": "/",
      "PrefetchCount": 20,
      "PublishConfirmTimeoutMs": 5000
    },
    "Postgres": {
      "ConnectionString": "Host=localhost;Port=5432;Database=logs2obs;Username=logs2obs;Password=logs2obs"
    },
    "Redis": {
      "ConnectionString": "localhost:6379",
      "InstanceName": "logs2obs:",
      "DefaultTtlSeconds": 3600
    },
    "Meilisearch": {
      "Url": "http://localhost:7700",
      "ApiKey": null,
      "IndexName": "logs2obs"
    },
    "DuckDb": {
      "DatabasePath": ":memory:",
      "MaxQueryTimeoutSeconds": 300
    },
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "ModelName": "llama3.2",
      "MaxTokens": 2048,
      "TimeoutSeconds": 60
    }
  }
}
```

### 3. Register Services

```csharp
using Logs2Obs.Adapters.Local.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Register all local adapters
builder.Services.AddLocalAdapters(builder.Configuration);

var app = builder.Build();
app.Run();
```

---

## 🏗️ Architecture

### Directory Structure

```
Logs2Obs.Adapters.Local/
├── Options/                 # Configuration POCOs
│   ├── MinioOptions.cs
│   ├── RabbitMqOptions.cs
│   ├── PostgresOptions.cs
│   ├── RedisOptions.cs
│   ├── MeilisearchOptions.cs
│   ├── DuckDbOptions.cs
│   └── OllamaOptions.cs
├── ObjectStore/             # Blob storage
│   └── MinioObjectStore.cs
├── MessageBus/              # Pub/sub messaging
│   ├── RabbitMqMessageBus.cs
│   └── InProcessChannelMessageBus.cs
├── MetadataStore/           # Key-value metadata
│   └── PostgresMetadataStore.cs
├── SchemaRegistry/          # Schema versioning
│   └── PostgresSchemaRegistry.cs
├── Search/                  # Full-text search
│   └── MeilisearchIndexer.cs
├── QueryEngine/             # Analytics queries
│   └── DuckDbQueryEngine.cs
├── Idempotency/             # Duplicate detection
│   └── RedisIdempotencyStore.cs
├── MatViews/                # Cached aggregations
│   └── RedisMatViewEngine.cs
├── Secrets/                 # Secret management
│   └── LocalSecretStore.cs
├── AI/                      # Natural language queries
│   └── OllamaAiService.cs
├── Scheduler/               # Job scheduling
│   └── QuartzScheduler.cs
└── DependencyInjection/
    └── LocalAdaptersServiceCollectionExtensions.cs
```

### Resilience Patterns

All external I/O operations use Polly resilience pipelines from `Logs2Obs.Core.Resilience`:

- **ForExternalIo<T>()**: 3 retries + circuit breaker (MinIO, Postgres, Redis, RabbitMQ)
- **ForSearch<T>()**: 2 retries + 10s timeout (Meilisearch)
- **ForStorage<T>()**: 3 retries + 60s timeout (large blob transfers)

Example:
```csharp
var pipeline = ResiliencePipelines.ForStorage<Stream?>();
return await pipeline.ExecuteAsync(async ct => {
    // MinIO read operation
}, cancellationToken);
```

---

## 🔍 Key Design Decisions

### 1. PostgreSQL Metadata Store Key Extraction

Uses convention-based key extraction (no `IKeyed` interface required):

1. Checks: `id`, `key`, `tenantId`, `queryId`, `jobId`, `ruleId`, `executionId`
2. Falls back to any property ending with `Id`
3. Throws `InvalidOperationException` if no key found

```csharp
await metadataStore.PutAsync("saved_queries", new SavedQuery {
    QueryId = "q1",  // ← automatically extracted as key
    Sql = "SELECT * FROM logs"
});
```

### 2. DuckDB Query Engine

- **In-memory by default** (`:memory:`)
- File-based option via `DatabasePath` configuration
- Query results stored as JSON in-memory (`ConcurrentDictionary`)
- Cost estimation returns placeholder values (local dev)

### 3. Meilisearch Aggregations

- SDK v0.x lacks native faceting API
- Fetches up to 10,000 results → LINQ aggregation in-memory
- **Not scalable to production** (acceptable for local dev)

### 4. RabbitMQ Publisher Confirms

- RabbitMQ.Client v7 uses simplified async API
- No `ConfirmSelectAsync()` or `WaitForConfirmsOrDieAsync()`
- Best-effort delivery for local dev

### 5. LocalSecretStore Warning

**⚠️ LOGS A WARNING ON EVERY ACCESS** to prevent accidental production use:
```
LocalSecretStore: GetSecretAsync(api-key) — this is an in-memory dev adapter. NEVER use in production.
```

---

## 📊 Testing

### Unit Testing with InProcessChannelMessageBus

```csharp
public class MyServiceTests
{
    [Fact]
    public async Task Should_Process_Messages()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IMessageBus, InProcessChannelMessageBus>();
        var bus = services.BuildServiceProvider().GetRequiredService<IMessageBus>();

        // Act
        await bus.PublishAsync("test-topic", new { Id = 1 });
        var envelope = await bus.SubscribeAsync<dynamic>("test-topic").FirstAsync();

        // Assert
        Assert.Equal(1, envelope.Payload.Id);
    }
}
```

### Integration Testing with Docker Compose

```bash
# Start local stack
docker compose up -d

# Run integration tests
dotnet test --filter "Category=Integration"

# Cleanup
docker compose down -v
```

---

## 🐛 Known Limitations

| Limitation | Impact | Workaround |
|------------|--------|------------|
| Meilisearch aggregations in-memory | Max 10k docs | Upgrade to v1.x SDK when available |
| DuckDB in-memory data loss | Data lost on restart | Use file-based mode for persistence |
| LocalSecretStore not encrypted | Secrets in plain memory | Use AWS Secrets Manager in production |
| RabbitMQ no publisher confirms | No delivery guarantees | Use SQS/SNS in production |
| Single-process only | No distributed scenarios | Deploy to Kubernetes for scale |

---

## 📝 Dependencies

```xml
<PackageReference Include="Minio" Version="6.*" />
<PackageReference Include="RabbitMQ.Client" Version="7.*" />
<PackageReference Include="Npgsql" Version="9.*" />
<PackageReference Include="Dapper" Version="2.*" />
<PackageReference Include="DuckDB.NET.Data.Full" Version="1.*" />
<PackageReference Include="StackExchange.Redis" Version="2.*" />
<PackageReference Include="Meilisearch" Version="0.*" />
<PackageReference Include="Quartz" Version="3.*" />
<PackageReference Include="Polly" Version="8.*" />
```

---

## 🚦 Production Readiness

**Status:** ❌ **NOT PRODUCTION-READY**

These adapters are optimized for:
- ✅ Local development
- ✅ Unit/integration testing
- ✅ Prototyping
- ✅ CI/CD pipelines

For production deployments, use `Logs2Obs.Adapters.Aws`:
- S3 → `S3ObjectStore`
- SQS/SNS → `SqsMessageBus`
- DynamoDB → `DynamoDbMetadataStore`
- OpenSearch → `OpenSearchIndexer`
- Athena → `AthenaQueryEngine`
- Secrets Manager → `AwsSecretStore`

---

## 📚 Related Projects

- **Logs2Obs.Core** - Core abstractions and models
- **Logs2Obs.Worker** - Background processing pipeline
- **Logs2Obs.Api** - REST API
- **Logs2Obs.Adapters.Aws** - AWS production adapters
- **Logs2Obs.Adapters.Azure** - Azure adapters (Phase 13)
- **Logs2Obs.Adapters.Gcp** - GCP adapters (Phase 13)

---

## 🤝 Contributing

This project is part of the Logs2Obs mono-repo. See `.squad/docs/LightScope_Design_v3.md` for architecture details.

**Key Files:**
- Design doc: `.squad/docs/LightScope_Design_v3.md`
- Decisions: `.squad/decisions/inbox/dolores-adapters-local-decisions.md`
- History: `.squad/agents/dolores/history.md`

---

## 📄 License

MIT License - See LICENSE file for details.
