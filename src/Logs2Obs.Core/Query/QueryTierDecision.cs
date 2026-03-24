namespace Logs2Obs.Core.Query;

using Logs2Obs.Core.Models;

public sealed record QueryTierDecision
{
    public required QueryTier Tier { get; init; }
    public required string Reason { get; init; }
    public string? Warning { get; init; }
    public IReadOnlyList<SubQuery>? SubQueries { get; init; }
}
