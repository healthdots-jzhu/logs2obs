namespace Logs2Obs.Adapters.Local.Options;

public sealed class DuckDbOptions
{
    public string DatabasePath { get; set; } = ":memory:";
    public int MaxQueryTimeoutSeconds { get; set; } = 300;
}
