namespace Logs2Obs.Core.Abstractions;

public sealed record AggBucket
{
    public required string Key { get; init; }
    public required long Count { get; init; }
    public IReadOnlyDictionary<string, object>? Extra { get; init; }
}
