namespace Logs2Obs.Core.Query;

using Logs2Obs.Core.Models;

public sealed record SubQuery
{
    public required QueryTier Tier { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
}
