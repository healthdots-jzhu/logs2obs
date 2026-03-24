namespace Logs2Obs.Core.Abstractions;

using Logs2Obs.Core.Models;

public sealed record QuerySubmitResult
{
    public string? ExecutionId { get; init; }
    public required QueryStatus Status { get; init; }
    public QueryCostEstimate? Estimate { get; init; }
    public string? ResultLocation { get; init; }
}
