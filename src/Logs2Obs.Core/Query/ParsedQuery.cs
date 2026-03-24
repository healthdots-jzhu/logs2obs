namespace Logs2Obs.Core.Query;

public sealed record ParsedQuery
{
    public required string QueryId { get; init; }
    public bool HasFullTextSearch { get; init; }
    public DateTimeOffset? EarliestTimestamp { get; init; }
    public DateTimeOffset? LatestTimestamp { get; init; }
    public bool HasTimeFilter { get; init; }
    public bool HasLimit { get; init; }
}
