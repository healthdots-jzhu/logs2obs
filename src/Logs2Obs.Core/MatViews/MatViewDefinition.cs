namespace Logs2Obs.Core.MatViews;

using Logs2Obs.Core.Models;

public sealed record MatViewDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Sql { get; init; }
    public required int RefreshIntervalSeconds { get; init; }
    public required MatViewStorage StorageTarget { get; init; }
    public required int RetentionMinutes { get; init; }
    public required GraphType SuggestedGraphType { get; init; }
}
