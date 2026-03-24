namespace Logs2Obs.Core.Models;

using System.Text.Json.Serialization;

public sealed class PullJobConfigDto
{
    [JsonPropertyName("jobId")]          public required string JobId { get; init; }
    [JsonPropertyName("tenantId")]       public required string TenantId { get; init; }
    [JsonPropertyName("connectorType")]  public required string ConnectorType { get; init; }
    [JsonPropertyName("schedule")]       public required string Schedule { get; init; }
    [JsonPropertyName("connectorConfig")] public required IReadOnlyDictionary<string, string> ConnectorConfig { get; init; }
    [JsonPropertyName("lastRunAt")]      public DateTimeOffset? LastRunAt { get; init; }
    [JsonPropertyName("isEnabled")]      public bool IsEnabled { get; init; }
}
