# Project Context

- **Owner:** Jason Zhu
- **Project:** logs2obs / LightScope — Lightweight Observability & Log Intelligence Service
- **Stack:** xUnit 2, Moq 4, Testcontainers 3, FluentAssertions 7, Docker Compose (for integration tests)
- **Design doc:** `.squad/docs/LightScope_Design_v3.md` (v3.0)
- **Created:** 2026-03-24

## Key Facts for My Work

- **My phase:** Phase 12 (Full Test Suite) — runs against Phases 1–11 outputs
- **Test projects:** LightScope.Core.Tests, LightScope.Api.Tests, LightScope.Worker.Tests, LightScope.Puller.Tests, LightScope.Integration.Tests
- **Assertions style:** FluentAssertions ALWAYS — `result.Should().Be(expected)` — never `Assert.Equal`
- **Test naming:** `{Method}_{Scenario}_{ExpectedResult}` — e.g., `Validate_WhenLogTypeIsInvalid_ReturnsFalse`
- **Required unit tests (from Section 27.10):**
  - SqlSafetyValidator: DROP, DELETE, INSERT, CREATE, ALTER, CROSS JOIN, no partition filter, no LIMIT, valid SELECT
  - QueryTierRouter: each of Hot / Warm / Cold / CrossTier routing rules
  - DtoMapper.ToDomain: TenantId never from DTO, Id always new UUIDv7, IngestedAt always UtcNow
  - ApiKeyAuthHandler: valid key (cache hit), valid key (DB hit), invalid key, inactive key, missing header
  - LogEntryDtoValidator: all validation rules from Section 27.9
- **Integration tests use Testcontainers:** real PostgreSQL (Npgsql), real Redis (StackExchange.Redis), real MinIO (S3-compat)
- **End-to-end test:** ingest batch → RabbitMQ → Worker → MinIO → DuckDB query → assert result
- **Test runner command:** `dotnet test` against the docker-compose local stack
- **Coverage targets:** Every interface has at least one test file; SqlSafetyValidator and QueryTierRouter are the highest-priority test targets

## Learnings

<!-- Append new learnings below. -->
