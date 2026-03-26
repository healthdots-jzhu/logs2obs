namespace Logs2Obs.Core.Models;

public sealed record ReplayStartedEvent
{
    public required string JobId { get; init; }
    public required string TenantId { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required ReplayOptions Options { get; init; }
}
