# Project Context

- **Owner:** Jason Zhu
- **Project:** logs2obs / LightScope — Lightweight Observability & Log Intelligence Service
- **Stack:** .NET 10, C# 14, ASP.NET Core Minimal APIs + gRPC, MediatR 12, FluentValidation 11, Polly 8, Serilog, OpenTelemetry, Parquet.Net
- **Design doc:** `.squad/docs/LightScope_Design_v3.md` (v3.0)
- **Created:** 2026-03-24

## Key Facts for My Work

- **My phases:** Phase 1 (LightScope.Core) and Phase 4 (LightScope.Api)
- **Core rule:** LightScope.Core references ONLY MediatR, FluentValidation, Parquet.Net, Polly, Microsoft.Extensions.*, System.* — never cloud SDKs
- **DtoMapper rule:** Id (UUIDv7), TenantId (from auth), IngestedAt (UtcNow) always set in DtoMapper.ToDomain() — never from DTO fields
- **API style:** Minimal APIs with endpoint route groups — no controllers ever
- **gRPC:** 3 streaming modes — unary (SendBatch), client streaming (StreamLogs), bidirectional (StreamWithAck)
- **Auth:** Dual — ApiKey (hashed, cached in Redis) + JWT/Cognito; TenantContextMiddleware extracts tenantId from both
- **Rate limiting:** Token bucket (burst) + sliding window (sustained), per-tenant, Section 11 of design doc
- **Validation:** FluentValidation validators for all DTOs before MediatR dispatch; LogEntryDtoValidator is the canonical example (Section 27.9)
- **Error hierarchy:** LightScopeException → ValidationException, SqlSafetyException, QueryGuardException, TenantNotFoundException, etc.
- **Protobuf:** `protos/log_ingestion.proto` — LogEntryProto, AckResponse, BatchRequest, SendResponse
- **Health endpoints:** /health/ready, /health/live, /metrics (Prometheus scrape endpoint)

## Learnings

<!-- Append new learnings below. -->
