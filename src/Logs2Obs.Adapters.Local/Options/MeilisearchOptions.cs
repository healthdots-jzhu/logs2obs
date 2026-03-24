namespace Logs2Obs.Adapters.Local.Options;

public sealed class MeilisearchOptions
{
    public string Url { get; set; } = "http://localhost:7700";
    public string? ApiKey { get; set; }
    public string IndexName { get; set; } = "logs2obs";
}
