namespace Logs2Obs.Core.Models;

public sealed record ReplayOptions
{
    public bool ReindexSearch { get; init; } = true;
    public bool ReprocessAlerts { get; init; } = false;
    public bool ReparseFiles { get; init; } = false;
    public string? OverrideParser { get; init; }
    public int MaxParallelFiles { get; init; } = 4;
}
