namespace Logs2Obs.Api.Options;

public sealed class OtelOptions
{
    public string ServiceName { get; init; } = "logs2obs-api";
    public string ServiceVersion { get; init; } = "1.0.0";
}
