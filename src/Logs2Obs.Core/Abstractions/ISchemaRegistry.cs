namespace Logs2Obs.Core.Abstractions;

using Logs2Obs.Core.Models;
using Logs2Obs.Core.Schema;

/// <summary>Registry for managing per-tenant schema versions and compatibility checks.</summary>
public interface ISchemaRegistry
{
    /// <summary>Returns the current schema version number for the given tenant.</summary>
    Task<uint> GetCurrentVersionAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Registers a new schema version for the given tenant and returns the new version number.</summary>
    Task<uint> RegisterSchemaAsync(string tenantId, IReadOnlyList<SchemaField> fields, CancellationToken ct = default);

    /// <summary>Validates whether the given log entry is compatible with the tenant's current schema.</summary>
    Task<bool> ValidateAsync(string tenantId, LogEntry entry, CancellationToken ct = default);

    /// <summary>Infers the schema from the given entry and registers it if new fields are detected.</summary>
    Task<uint> InferAndRegisterAsync(string tenantId, LogEntry entry, CancellationToken ct = default);
}
