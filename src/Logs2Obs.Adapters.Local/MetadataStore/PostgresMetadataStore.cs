namespace Logs2Obs.Adapters.Local.MetadataStore;

using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Resilience;
using Logs2Obs.Adapters.Local.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

public sealed class PostgresMetadataStore(
    IOptions<PostgresOptions> options,
    ILogger<PostgresMetadataStore>? logger = null) : IMetadataStore
{
    private readonly string _connectionString = options.Value.ConnectionString;
    private readonly ILogger<PostgresMetadataStore> _logger =
        logger ?? NullLogger<PostgresMetadataStore>.Instance;

    public async Task<T?> GetAsync<T>(string table, string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<T?>();
        return await pipeline.ExecuteAsync(async token =>
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await EnsureTableAsync(conn, table, token);
            var sql = $"SELECT value FROM metadata_{SanitizeName(table)} WHERE key = @key";
            var json = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(sql, new { key }, cancellationToken: token));
            if (json is null) return default;
            return JsonSerializer.Deserialize<T>(json);
        }, ct);
    }

    public async Task PutAsync<T>(string table, T entity, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await EnsureTableAsync(conn, table, token);
            var key = ExtractKey(entity);
            var json = JsonSerializer.Serialize(entity);
            var sql = $"""
                INSERT INTO metadata_{SanitizeName(table)} (key, value, updated_at)
                VALUES (@key, @json::jsonb, NOW())
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = NOW()
                """;
            await conn.ExecuteAsync(new CommandDefinition(sql, new { key, json }, cancellationToken: token));
            return true;
        }, ct);
    }

    public async Task DeleteAsync(string table, string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            var sql = $"DELETE FROM metadata_{SanitizeName(table)} WHERE key = @key";
            await conn.ExecuteAsync(new CommandDefinition(sql, new { key }, cancellationToken: token));
            return true;
        }, ct);
    }

    public async IAsyncEnumerable<T> QueryAsync<T>(string table, Func<T, bool> filter, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await EnsureTableAsync(conn, table, ct);
        var sql = $"SELECT value FROM metadata_{SanitizeName(table)}";
        var rows = await conn.QueryAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
        foreach (var json in rows)
        {
            ct.ThrowIfCancellationRequested();
            var entity = JsonSerializer.Deserialize<T>(json);
            if (entity is not null && filter(entity))
                yield return entity;
        }
    }

    private static async Task EnsureTableAsync(NpgsqlConnection conn, string table, CancellationToken ct)
    {
        var sanitized = SanitizeName(table);
        var ddl = $"""
            CREATE TABLE IF NOT EXISTS metadata_{sanitized} (
                key TEXT PRIMARY KEY,
                value JSONB NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """;
        await conn.ExecuteAsync(new CommandDefinition(ddl, cancellationToken: ct));
    }

    private static string SanitizeName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, "[^a-zA-Z0-9_]", "_").ToLowerInvariant();

    private static string ExtractKey<T>(T entity)
    {
        var node = JsonSerializer.SerializeToNode(entity)?.AsObject();
        if (node is null) throw new InvalidOperationException("Cannot serialize entity to extract key.");

        foreach (var candidate in new[] { "id", "key", "tenantId", "queryId", "jobId", "ruleId", "executionId" })
        {
            if (node.TryGetPropertyValue(candidate, out var val) && val is not null)
                return val.GetValue<string>();
        }

        foreach (var prop in node)
        {
            if (prop.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && prop.Value is not null)
                return prop.Value.GetValue<string>();
        }

        throw new InvalidOperationException(
            $"Cannot derive key from {typeof(T).Name}. Add a property named 'Id', 'Key', or '*Id'.");
    }
}
