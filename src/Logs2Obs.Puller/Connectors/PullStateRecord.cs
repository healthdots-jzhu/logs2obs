namespace Logs2Obs.Puller.Connectors;

using System.Text.Json.Serialization;

internal sealed record PullStateRecord
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    public required Dictionary<string, string> State { get; init; }

    public static string BuildKey(string jobId) => $"pullstate:{jobId}";
}
