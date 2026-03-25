namespace Logs2Obs.Api.Options;

public sealed class RateLimiterOptions
{
    public int IngestTokenLimit { get; init; } = 1000;
    public int IngestTokensPerPeriod { get; init; } = 500;
    public int QueryPermitLimit { get; init; } = 100;
}
