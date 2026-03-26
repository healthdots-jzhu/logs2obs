namespace Logs2Obs.Core.Models;

public sealed record QueryContext
{
    public required string TenantId { get; init; }
    public IReadOnlyList<string> Environments { get; init; } = [];
    public IReadOnlyList<string> KnownSources { get; init; } = [];
    public IReadOnlyList<string> LogTypes { get; init; } = [];
    public int HotRetentionDays { get; init; } = 7;
    public int WarmRetentionDays { get; init; } = 90;
}
