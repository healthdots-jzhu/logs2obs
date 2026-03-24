namespace Logs2Obs.Core.Abstractions;

using Logs2Obs.Core.Models;

public sealed record AiSqlResult
{
    public required string Sql { get; init; }
    public required string Explanation { get; init; }
    public required GraphType SuggestedGraphType { get; init; }
    public required int InputTokenCount { get; init; }
    public required int OutputTokenCount { get; init; }
    public required string ModelUsed { get; init; }
}
