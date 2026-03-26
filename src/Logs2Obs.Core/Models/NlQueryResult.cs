namespace Logs2Obs.Core.Models;

public sealed record NlQueryResult
{
    public required string Sql { get; init; }
    public required string Explanation { get; init; }
    public required GraphType SuggestedGraphType { get; init; }
    public IReadOnlyList<string> SafetyWarnings { get; init; } = [];
}
