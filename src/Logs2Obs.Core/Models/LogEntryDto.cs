namespace Logs2Obs.Core.Models;

using System.Text.Json.Serialization;

public sealed class LogEntryDto
{
    [JsonPropertyName("sourceId")]    public required string SourceId { get; init; }
    [JsonPropertyName("logType")]     public required string LogType { get; init; }
    [JsonPropertyName("level")]       public required string Level { get; init; }
    [JsonPropertyName("environment")] public required string Environment { get; init; }
    [JsonPropertyName("category")]    public string? Category { get; init; }
    [JsonPropertyName("timestamp")]   public required DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("message")]     public required string Message { get; init; }
    [JsonPropertyName("traceId")]     public string? TraceId { get; init; }
    [JsonPropertyName("stackTrace")]  public string? StackTrace { get; init; }
    [JsonPropertyName("tags")]        public IReadOnlyDictionary<string, string>? Tags { get; init; }
    [JsonPropertyName("metric")]      public MetricDto? Metric { get; init; }
}
