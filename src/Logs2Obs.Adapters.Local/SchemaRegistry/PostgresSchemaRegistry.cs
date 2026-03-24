namespace Logs2Obs.Adapters.Local.SchemaRegistry;

using System.Text.Json;
using Dapper;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Resilience;
using Logs2Obs.Core.Schema;
using Logs2Obs.Adapters.Local.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

public sealed class PostgresSchemaRegistry(
    IOptions<PostgresOptions> options,
    ILogger<PostgresSchemaRegistry> logger) : ISchemaRegistry
{
    private readonly string _connectionString = options.Value.ConnectionString;

    public async Task<uint> GetCurrentVersionAsync(string tenantId, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<uint>();
        return await pipeline.ExecuteAsync(async token =>
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await EnsureTableAsync(conn, token);
            const string sql = "SELECT COALESCE(MAX(version), 0) FROM schema_registry WHERE tenant_id = @tenantId";
            var result = await conn.QuerySingleAsync<uint>(new CommandDefinition(sql, new { tenantId }, cancellationToken: token));
            return result;
        }, ct);
    }

    public async Task<uint> RegisterSchemaAsync(string tenantId, IReadOnlyList<SchemaField> fields, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<uint>();
        return await pipeline.ExecuteAsync(async token =>
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await EnsureTableAsync(conn, token);
            var currentVersion = await GetCurrentVersionAsync(tenantId, token);
            var nextVersion = currentVersion + 1;
            var fieldsJson = JsonSerializer.Serialize(fields);
            const string sql = """
                INSERT INTO schema_registry (tenant_id, version, fields, registered_at)
                VALUES (@tenantId, @version, @fieldsJson::jsonb, NOW())
                """;
            await conn.ExecuteAsync(new CommandDefinition(sql, new { tenantId, version = nextVersion, fieldsJson }, cancellationToken: token));
            logger.LogInformation("Registered schema v{Version} for tenant {TenantId}", nextVersion, tenantId);
            return nextVersion;
        }, ct);
    }

    public async Task<bool> ValidateAsync(string tenantId, LogEntry entry, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var currentVersion = await GetCurrentVersionAsync(tenantId, token);
            if (currentVersion == 0) return true;

            await using var conn = new NpgsqlConnection(_connectionString);
            const string sql = "SELECT fields FROM schema_registry WHERE tenant_id = @tenantId AND version = @version";
            var fieldsJson = await conn.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(sql, new { tenantId, version = currentVersion }, cancellationToken: token));

            if (fieldsJson is null) return true;

            var fields = JsonSerializer.Deserialize<List<SchemaField>>(fieldsJson);
            if (fields is null) return true;

            var entryTags = entry.Tags ?? new Dictionary<string, string>();
            var requiredFields = fields.Where(f => !f.IsNullable).ToList();
            return requiredFields.All(f => entryTags.ContainsKey(f.Name));
        }, ct);
    }

    public async Task<uint> InferAndRegisterAsync(string tenantId, LogEntry entry, CancellationToken ct = default)
    {
        var inferredFields = SchemaInferenceEngine.InferSchema([entry]);
        return await RegisterSchemaAsync(tenantId, inferredFields, ct);
    }

    private static async Task EnsureTableAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS schema_registry (
                id SERIAL PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                version INT NOT NULL,
                fields JSONB NOT NULL,
                registered_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (tenant_id, version)
            )
            """;
        await conn.ExecuteAsync(new CommandDefinition(ddl, cancellationToken: ct));
    }
}
