namespace Logs2Obs.Core.Models;

public sealed record PullJobConfig
{
    public required string JobId { get; init; }
    public required string TenantId { get; init; }
    public required ConnectorType ConnectorType { get; init; }
    public required string Schedule { get; init; }
    public required IReadOnlyDictionary<string, string> ConnectorConfig { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public bool IsEnabled { get; init; }
}
