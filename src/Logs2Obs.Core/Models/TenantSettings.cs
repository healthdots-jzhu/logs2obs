namespace Logs2Obs.Core.Models;

public sealed record TenantSettings
{
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public int HotRetentionDays { get; init; } = 3;
    public int WarmRetentionDays { get; init; } = 90;
    public double MaxQueryScanGb { get; init; } = 10.0;
    public bool RequireTimeFilter { get; init; }
    public bool RequireLimit { get; init; }
    public bool IsActive { get; init; } = true;
}
