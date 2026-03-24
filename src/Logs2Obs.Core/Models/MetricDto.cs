namespace Logs2Obs.Core.Models;

using System.Text.Json.Serialization;

public sealed class MetricDto
{
    [JsonPropertyName("metricName")] public required string MetricName { get; init; }
    [JsonPropertyName("unit")]       public required string Unit { get; init; }
    [JsonPropertyName("value")]      public required double Value { get; init; }
    [JsonPropertyName("metricType")] public required string MetricType { get; init; }
}
