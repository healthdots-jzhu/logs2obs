# logs2obs Local Development

## Prerequisites

Install the following tools before starting:

- **Docker Desktop 4.x** — Required for local infrastructure (MinIO, RabbitMQ, PostgreSQL, Redis, MeiliSearch)
- **.NET 10 SDK** — Download from [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Git** — Version 2.30 or later

**System Requirements:**
- **RAM:** 8 GB minimum (16 GB recommended if running with `--profile ai` for Ollama)
- **Disk:** 10 GB free space for Docker volumes
- **OS:** Windows 10/11, macOS 12+, or Linux (Ubuntu 20.04+)

---

## Setup Steps

### 1. Clone Repository

```bash
git clone https://github.com/your-org/logs2obs.git
cd logs2obs
```

### 2. Start Local Stack

The `docker-compose.yml` file in `docker/` directory starts all required services:

```bash
cd docker
docker compose up -d
```

**Services started:**
- **MinIO** (port 9000) — S3-compatible object store for Parquet files
- **RabbitMQ** (port 5672, management 15672) — Message bus for fanout
- **PostgreSQL** (port 5432) — Metadata store and schema registry
- **Redis** (port 6379) — Cache and idempotency store
- **MeiliSearch** (port 7700) — Full-text search index
- **logs2obs-api** (port 8080) — Main API service
- **logs2obs-worker** (port 5000) — Background worker
- **logs2obs-puller** (port 5001) — Pull connector service
- **logs2obs-queryengine** (port 8081) — Query execution service

**Optional profiles:**

```bash
# Start with Ollama (local AI — heavy, requires GPU)
docker compose --profile ai up -d

# Start with Prometheus + Grafana monitoring
docker compose --profile monitoring up -d
```

**Wait for health checks:**

```bash
docker compose ps
```

All services should show `healthy` status. If any service is stuck in `starting`, check logs:

```bash
docker compose logs -f minio  # Example: check MinIO logs
```

### 3. Build Solution

```bash
cd ..  # Back to repo root
dotnet build logs2obs.slnx
```

**Expected output:**

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### 4. Run API Service

```bash
dotnet run --project src/Logs2Obs.Api
```

**Expected output:**

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:8080
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

### 5. Verify Health

```bash
curl http://localhost:8080/health/ready
```

**Expected response:**

```json
{
  "status": "Healthy",
  "checks": {
    "postgres": "Healthy",
    "rabbitmq": "Healthy",
    "minio": "Healthy",
    "redis": "Healthy",
    "meilisearch": "Healthy"
  }
}
```

**If any check is `Unhealthy`**, see [Troubleshooting](#troubleshooting) section.

---

## Environment Variables

logs2obs configuration is environment-driven. For local development, defaults are set in `appsettings.Development.json`. Override with environment variables if needed.

### Essential Variables

| Variable | Default (Local) | Description |
|---|---|---|
| `Logs2Obs__Provider` | `Local` | Provider selection: `Local` \| `Aws` \| `Azure` \| `Gcp` |
| `ConnectionStrings__Postgres` | `Host=localhost;Database=logs2obs;Username=logs2obs;Password=logs2obs` | PostgreSQL connection string |
| `ObjectStore__MinIO__Endpoint` | `http://localhost:9000` | MinIO S3 endpoint |
| `ObjectStore__MinIO__AccessKey` | `minioadmin` | MinIO access key |
| `ObjectStore__MinIO__SecretKey` | `minioadmin` | MinIO secret key |
| `MessageBus__RabbitMq__Host` | `localhost` | RabbitMQ host |
| `MessageBus__RabbitMq__Port` | `5672` | RabbitMQ port |
| `MessageBus__RabbitMq__Username` | `guest` | RabbitMQ username |
| `MessageBus__RabbitMq__Password` | `guest` | RabbitMQ password |
| `Search__MeiliSearch__Endpoint` | `http://localhost:7700` | MeiliSearch endpoint |
| `Metadata__Redis__ConnectionString` | `localhost:6379` | Redis connection string |
| `Logs2Obs__HotRetentionDays` | `3` | OpenSearch retention (days) |
| `Logs2Obs__WarmRetentionDays` | `90` | S3 Standard retention (days) |
| `Logs2Obs__ColdRetentionDays` | `730` | S3 Glacier retention (days) |

### AI Configuration (Optional)

If using Ollama (local AI) or GitHub Models:

| Variable | Default | Description |
|---|---|---|
| `Ai__Provider` | `Ollama` | AI provider: `Ollama` \| `GitHubModels` \| `None` |
| `Ai__Ollama__Endpoint` | `http://localhost:11434` | Ollama API endpoint |
| `Ai__Ollama__Model` | `llama3` | Ollama model name |
| `Ai__GitHubModels__ApiKey` | — | GitHub Models API key (required if using GitHub Models) |
| `Ai__GitHubModels__Model` | `openai/gpt-4o` | GitHub Models model name |

**Pull Ollama model (required for local AI):**

```bash
docker exec -it ollama ollama pull llama3
```

### Observability Configuration (Optional)

For Prometheus + Grafana monitoring:

| Variable | Default | Description |
|---|---|---|
| `OpenTelemetry__Enabled` | `true` | Enable OpenTelemetry traces + metrics |
| `OpenTelemetry__PrometheusEndpoint` | `http://localhost:9090` | Prometheus scrape endpoint |
| `Grafana__Endpoint` | `http://localhost:3000` | Grafana UI URL |

**Access Grafana:**

- **URL:** `http://localhost:3000`
- **Username:** `admin`
- **Password:** `admin`

Pre-built dashboards are provisioned from `infra/grafana/dashboards/`.

---

## Running Tests

### Unit Tests

Run all unit tests (no infrastructure required):

```bash
dotnet test logs2obs.slnx --filter "Category!=Integration"
```

### Integration Tests

Integration tests require docker-compose services to be running:

```bash
# Ensure docker-compose is up
cd docker
docker compose up -d
cd ..

# Run integration tests
dotnet test logs2obs.slnx --filter "Category=Integration"
```

### Exclude Local Adapter Tests (CI)

If running in CI without Docker:

```bash
dotnet test logs2obs.slnx --configuration Release --filter "FullyQualifiedName!~Adapters.Local"
```

### Run with Code Coverage

```bash
dotnet test logs2obs.slnx --collect:"XPlat Code Coverage" --results-directory ./coverage
```

Coverage report: `./coverage/*/coverage.cobertura.xml`

---

## Switching Providers

logs2obs supports multiple cloud providers. Switch by changing the `Logs2Obs__Provider` environment variable.

### Switch to AWS

```bash
export Logs2Obs__Provider=Aws

# AWS-specific configuration
export AWS_REGION=us-east-1
export ObjectStore__S3__BucketName=my-logs2obs-bucket
export MessageBus__Sns__TopicArn=arn:aws:sns:us-east-1:123456789012:logs2obs-ingest
export QueryEngine__Athena__DatabaseName=logs2obs
export QueryEngine__Athena__WorkGroup=logs2obs-workgroup
export Search__OpenSearch__Endpoint=https://my-opensearch.us-east-1.es.amazonaws.com
export Metadata__DynamoDB__TableName=logs2obs-metadata
export Cache__ElastiCache__Endpoint=my-redis.abc123.cfg.usw2.cache.amazonaws.com:6379
```

**Rebuild and run:**

```bash
dotnet build logs2obs.slnx
dotnet run --project src/Logs2Obs.Api
```

### Switch to Azure

```bash
export Logs2Obs__Provider=Azure

# Azure-specific configuration
export ObjectStore__BlobStorage__ConnectionString="DefaultEndpointsProtocol=https;AccountName=..."
export MessageBus__ServiceBus__ConnectionString="Endpoint=sb://..."
export QueryEngine__Synapse__ServerName=my-synapse.sql.azuresynapse.net
export Metadata__CosmosDB__Endpoint=https://my-cosmosdb.documents.azure.com:443/
export Metadata__CosmosDB__Key=your_primary_key_here
```

### Switch to GCP

```bash
export Logs2Obs__Provider=Gcp

# GCP-specific configuration
export GOOGLE_APPLICATION_CREDENTIALS=/path/to/service-account.json
export ObjectStore__Gcs__BucketName=my-logs2obs-bucket
export MessageBus__PubSub__ProjectId=my-gcp-project
export QueryEngine__BigQuery__ProjectId=my-gcp-project
export QueryEngine__BigQuery__DatasetId=logs2obs
```

---

## Common Workflows

### Ingest Sample Logs

```bash
curl -X POST http://localhost:8080/api/v1/logs \
  -H "X-Api-Key: ls_dev_key_12345" \
  -H "Content-Type: application/json" \
  -d '{
    "entries": [{
      "sourceId": "test-service",
      "logType": "Application",
      "level": "Info",
      "environment": "dev",
      "category": "request",
      "timestamp": "2026-03-23T14:32:00Z",
      "message": "User logged in",
      "tags": {"userId": "USR-123"}
    }]
  }'
```

### Query Logs

```bash
curl -X POST http://localhost:8080/api/v1/query/sql \
  -H "X-Api-Key: ls_dev_key_12345" \
  -H "Content-Type: application/json" \
  -d '{
    "sql": "SELECT * FROM logs WHERE environment='\''dev'\'' AND year='\''2026'\'' AND month='\''03'\'' AND day='\''23'\'' LIMIT 10"
  }'
```

### Check Worker Queue Lag

```bash
# RabbitMQ Management UI
open http://localhost:15672
# Username: guest, Password: guest
# Navigate to "Queues" tab — check `ls-storage-writer` queue depth
```

### View Parquet Files in MinIO

```bash
# MinIO Console
open http://localhost:9001
# Username: minioadmin, Password: minioadmin
# Navigate to "logs2obs-logs" bucket
```

### Inspect OpenSearch/MeiliSearch Index

```bash
# MeiliSearch
curl http://localhost:7700/indexes/logs2obs-logs/stats
```

---

## Troubleshooting

### Issue: MinIO Connection Refused

**Symptom:** API logs show `Connection refused at http://localhost:9000`

**Solution:**

1. Check MinIO is running:

```bash
docker compose ps minio
```

2. If stopped, restart:

```bash
docker compose up -d minio
```

3. If still failing, check MinIO logs:

```bash
docker compose logs minio
```

4. Common fix: MinIO data volume corrupted. **Reset volume:**

```bash
docker compose down -v  # WARNING: deletes all data
docker compose up -d
```

---

### Issue: OpenSearch/MeiliSearch Heap Out of Memory

**Symptom:** MeiliSearch container crashes or logs show `OutOfMemoryError`

**Solution:**

1. Increase Docker Desktop memory allocation:
   - Docker Desktop → Settings → Resources → Memory → Set to 8 GB minimum

2. Reduce index size (delete old indices):

```bash
curl -X DELETE http://localhost:7700/indexes/logs2obs-logs
```

3. Restart MeiliSearch:

```bash
docker compose restart meilisearch
```

---

### Issue: DuckDB "Parquet Path Not Found"

**Symptom:** Query returns `No such file or directory: s3://logs2obs-logs/tenant=...`

**Solution:**

1. Verify Parquet files exist in MinIO:

```bash
# MinIO Console → Buckets → logs2obs-logs
open http://localhost:9001
```

2. Check Worker successfully wrote files (check logs):

```bash
docker compose logs logs2obs-worker | grep "Parquet written"
```

3. If no files, check Worker queue consumption:

```bash
curl http://localhost:15672/api/queues/%2F/ls-storage-writer \
  -u guest:guest
```

4. If queue is backing up, scale Worker:

```bash
docker compose up -d --scale logs2obs-worker=3
```

---

### Issue: PostgreSQL "Connection Refused"

**Symptom:** API fails to start with `Npgsql.NpgsqlException: Connection refused`

**Solution:**

1. Check PostgreSQL is running:

```bash
docker compose ps postgres
```

2. Test connection manually:

```bash
docker exec -it postgres psql -U logs2obs -d logs2obs -c "SELECT 1;"
```

3. If database doesn't exist, recreate:

```bash
docker compose down postgres
docker compose up -d postgres
```

---

### Issue: RabbitMQ Queue Not Consuming

**Symptom:** Logs ingested but Worker logs show no activity; RabbitMQ queue depth increasing

**Solution:**

1. Check Worker is running:

```bash
docker compose ps logs2obs-worker
```

2. Check Worker logs for errors:

```bash
docker compose logs -f logs2obs-worker
```

3. Manually consume one message (test):

```bash
# RabbitMQ Management UI → Queues → ls-storage-writer → Get Messages
open http://localhost:15672
```

4. If Worker repeatedly crashes, check for poison message in DLQ:

```bash
# Navigate to DLQ: ls-storage-writer-dlq
# If messages present, inspect payload and fix validation issue
```

---

### Issue: Rate Limit "Too Many Requests" in Local Dev

**Symptom:** `429 Too Many Requests` when testing locally

**Solution:**

Disable rate limiting for local development by commenting out in `Program.cs`:

```csharp
// app.UseRateLimiter();  // Comment this line for local dev
```

Or increase limits in `appsettings.Development.json`:

```json
{
  "RateLimiting": {
    "IngestionTokensPerMinute": 100000,
    "QueryRequestsPerMinute": 1000
  }
}
```

---

### Issue: Slow Query Execution in Local Dev

**Symptom:** Queries take >10 seconds even on small datasets

**Solution:**

1. Ensure DuckDB is using local files (not fetching from MinIO network):

```bash
# Check DuckDB query plan
dotnet run --project src/Logs2Obs.QueryEngine -- explain "SELECT * FROM logs LIMIT 10"
```

2. Verify Parquet files are partitioned correctly:

```bash
# Files should be under: s3://logs2obs-logs/tenant={tenantId}/logtype={logType}/env={env}/year={yyyy}/month={MM}/day={dd}/
```

3. Add partition filters to SQL:

```sql
-- Slow (full table scan)
SELECT * FROM logs WHERE message LIKE '%error%' LIMIT 100;

-- Fast (partition pruning)
SELECT * FROM logs WHERE message LIKE '%error%' AND year='2026' AND month='03' AND day='23' LIMIT 100;
```

---

## Development Tips

### Hot Reload

Use `dotnet watch` for automatic rebuild on file changes:

```bash
dotnet watch run --project src/Logs2Obs.Api
```

### Debug with VS Code

Add to `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": ".NET Core Launch (API)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/src/Logs2Obs.Api/bin/Debug/net10.0/Logs2Obs.Api.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/Logs2Obs.Api",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "Logs2Obs__Provider": "Local"
      }
    }
  ]
}
```

### Generate Test Data

Use the included test data generator:

```bash
dotnet run --project tests/Logs2Obs.TestDataGenerator -- --count 10000 --output test-logs.json
```

Then ingest:

```bash
curl -X POST http://localhost:8080/api/v1/logs/bulk \
  -H "X-Api-Key: ls_dev_key_12345" \
  -F "file=@test-logs.json"
```

### Clean Up Volumes (Full Reset)

```bash
cd docker
docker compose down -v  # WARNING: deletes all data (MinIO, PostgreSQL, Redis)
docker compose up -d
```

---

## Useful Commands

### Docker Compose

```bash
# Start all services
docker compose up -d

# Stop all services
docker compose down

# View logs
docker compose logs -f <service>

# Restart a service
docker compose restart <service>

# Scale Worker
docker compose up -d --scale logs2obs-worker=5
```

### Database

```bash
# Connect to PostgreSQL
docker exec -it postgres psql -U logs2obs -d logs2obs

# Dump schema
docker exec postgres pg_dump -U logs2obs -d logs2obs --schema-only > schema.sql

# Check Redis keys
docker exec -it redis redis-cli KEYS "idempotency:*"
```

### MinIO

```bash
# Create bucket (if not exists)
docker exec minio mc mb local/logs2obs-logs

# List objects
docker exec minio mc ls local/logs2obs-logs --recursive
```

### RabbitMQ

```bash
# List queues
docker exec rabbitmq rabbitmqctl list_queues

# Purge queue
docker exec rabbitmq rabbitmqctl purge_queue ls-storage-writer
```

---

## Next Steps

- **Read [API Reference](api-reference.md)** for full endpoint documentation
- **Read [Architecture](architecture.md)** for system design deep-dive
- **Deploy to AWS:** See `infra/aws/README.md` for CDK deployment guide
- **Contribute:** See `CONTRIBUTING.md` for contribution guidelines

---

## Support

- **Internal:** Slack #logs2obs-dev
- **Issues:** GitHub Issues at `https://github.com/your-org/logs2obs/issues`
- **Email:** Jason Zhu <jason@example.com>
