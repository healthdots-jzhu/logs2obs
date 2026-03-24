namespace Logs2Obs.Core.Models;

public sealed record LogEntry
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required LogType LogType { get; init; }
    public required LogLevel Level { get; init; }
    public required string Environment { get; init; }
    public string? Category { get; init; }
    public required long TimestampUnixMs { get; init; }
    public required string Message { get; init; }
    public string? TraceId { get; init; }
    public string? StackTrace { get; init; }
    public IReadOnlyDictionary<string, string>? Tags { get; init; }
    public MetricEntry? Metric { get; init; }
    public required string TenantId { get; init; }
    public required DateTimeOffset IngestedAt { get; init; }
    public required IngestionMode IngestionMode { get; init; }
    public uint SchemaVersion { get; init; } = 1;
}
