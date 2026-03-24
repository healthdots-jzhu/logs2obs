namespace Logs2Obs.Core.Graphs;

using Logs2Obs.Core.Models;

public sealed record GraphSuggestion
{
    public required GraphType GraphType { get; init; }
    public required double Confidence { get; init; }
    public required string Reason { get; init; }
}
