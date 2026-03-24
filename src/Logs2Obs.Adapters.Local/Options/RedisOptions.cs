namespace Logs2Obs.Adapters.Local.Options;

public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "logs2obs:";
    public int DefaultTtlSeconds { get; set; } = 3600;
}
