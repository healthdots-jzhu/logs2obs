namespace Logs2Obs.Core.Abstractions;

public sealed record QueryCostEstimate
{
    public required double EstimatedScanGb { get; init; }
    public required double EstimatedCostUsd { get; init; }
    public required string ConfidenceLevel { get; init; }
}
