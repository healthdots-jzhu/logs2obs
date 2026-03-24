namespace Logs2Obs.Core.Abstractions;

public sealed record MatViewResult
{
    public required bool IsFresh { get; init; }
    public required IReadOnlyList<IReadOnlyDictionary<string, object>> Data { get; init; }
    public required string Source { get; init; }
}
