namespace Logs2Obs.QueryEngine.Options;

public sealed class QueryEngineOptions
{
    public double DefaultConfirmCostThresholdUsd { get; set; } = 1.0;
    public double MaxScanGbHardLimit { get; set; } = 100.0;
    public int QueryTimeoutSeconds { get; set; } = 300;
}
