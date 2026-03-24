# logs2obs

> Lightweight observability & log intelligence — enabling logs for observability.

## Quick Start

```bash
# Start all local services
cd docker
docker compose up -d

# Bootstrap (create buckets, validate connections)
bash infra/scripts/local-setup.sh

# Run the API
dotnet run --project src/Logs2Obs.Api

# Run tests
dotnet test
```

## Services

| Service | URL | Credentials |
|---------|-----|-------------|
| MinIO Console | http://localhost:9001 | minioadmin / minioadmin |
| RabbitMQ Management | http://localhost:15672 | guest / guest |
| Meilisearch | http://localhost:7700 | — |
| PostgreSQL | localhost:5432 | logs2obs / logs2obs |
| Redis | localhost:6379 | — |

## Optional Profiles

```bash
# Start with Ollama (local AI — heavy, GPU recommended)
docker compose --profile ai up -d

# Start with Prometheus + Grafana monitoring
docker compose --profile monitoring up -d
```

## Architecture

See `.squad/docs/LightScope_Design_v3.md` for the full design document.

## Team

Built with Squad AI team. See `.squad/team.md`.
