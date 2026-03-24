#!/usr/bin/env bash
set -euo pipefail

echo "🚀 logs2obs local setup"
echo "========================"

# Wait for MinIO
echo "⏳ Waiting for MinIO..."
until curl -sf http://localhost:9000/minio/health/live > /dev/null 2>&1; do sleep 2; done
echo "✅ MinIO ready"

# Create MinIO buckets
echo "📦 Creating logs2obs buckets..."
docker run --rm --network host \
  --entrypoint "" \
  minio/mc:latest \
  sh -c "mc alias set local http://localhost:9000 minioadmin minioadmin && \
         mc mb --ignore-existing local/logs2obs && \
         mc mb --ignore-existing local/logs2obs-ai-audit && \
         echo 'Buckets ready'"

# Wait for PostgreSQL
echo "⏳ Waiting for PostgreSQL..."
until docker exec $(docker ps -qf "ancestor=postgres:17") pg_isready -U logs2obs > /dev/null 2>&1; do sleep 2; done
echo "✅ PostgreSQL ready"

# Wait for Redis
echo "⏳ Waiting for Redis..."
until docker exec $(docker ps -qf "ancestor=redis:7-alpine") redis-cli ping > /dev/null 2>&1; do sleep 2; done
echo "✅ Redis ready"

# Wait for Meilisearch
echo "⏳ Waiting for Meilisearch..."
until curl -sf http://localhost:7700/health > /dev/null 2>&1; do sleep 2; done
echo "✅ Meilisearch ready"

# Pull Ollama model (only if ollama profile is active)
if curl -sf http://localhost:11434/api/tags > /dev/null 2>&1; then
  echo "🤖 Pulling Ollama llama3.2 model (this may take a while)..."
  curl -s http://localhost:11434/api/pull -d '{"name":"llama3.2"}' | tail -1
  echo "✅ Ollama model ready"
fi

echo ""
echo "✅ logs2obs local stack is ready!"
echo "   MinIO console:   http://localhost:9001  (minioadmin/minioadmin)"
echo "   RabbitMQ UI:     http://localhost:15672 (guest/guest)"
echo "   Meilisearch:     http://localhost:7700"
echo "   PostgreSQL:      localhost:5432 (logs2obs/logs2obs)"
echo "   Redis:           localhost:6379"
