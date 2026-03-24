namespace Logs2Obs.Core.Abstractions;

public sealed record SqlSafetyReport
{
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
