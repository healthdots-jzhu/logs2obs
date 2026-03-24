namespace Logs2Obs.Core.Models;

public sealed record MetricEntry
{
    public required string MetricName { get; init; }
    public required string Unit { get; init; }
    public required double Value { get; init; }
    public required MetricType MetricType { get; init; }
}
