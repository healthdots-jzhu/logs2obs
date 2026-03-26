namespace Logs2Obs.QueryEngine.Options;

public sealed class QueryEngineOptions
{
    public double DefaultConfirmCostThresholdUsd { get; set; } = 1.0;
    public double MaxScanGbHardLimit { get; set; } = 100.0;
    public int QueryTimeoutSeconds { get; set; } = 300;
    public string AlertEvaluatorQueue { get; set; } = "ls-alert-evaluator";
    public string MatViewRefreshQueue { get; set; } = "ls-matview-refresh";
    public string SystemEventsQueue { get; set; } = "lightscope-system-events";
    public string IngestQueue { get; set; } = "lightscope-ingest";
    public string ReplayObjectPrefix { get; set; } = "logs";
}
