namespace Logs2Obs.Adapters.Local.Options;

public sealed class PostgresOptions
{
    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=logs2obs;Username=logs2obs;Password=logs2obs";
}
