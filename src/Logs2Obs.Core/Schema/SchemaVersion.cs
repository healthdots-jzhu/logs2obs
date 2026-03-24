namespace Logs2Obs.Core.Schema;

public sealed record SchemaVersion
{
    public required string TenantId { get; init; }
    public required uint Version { get; init; }
    public required IReadOnlyList<SchemaField> Fields { get; init; }
    public required DateTimeOffset RegisteredAt { get; init; }
}
