namespace Logs2Obs.Core.Abstractions;

public sealed record SearchAggResult
{
    public required IReadOnlyList<AggBucket> Buckets { get; init; }
}
