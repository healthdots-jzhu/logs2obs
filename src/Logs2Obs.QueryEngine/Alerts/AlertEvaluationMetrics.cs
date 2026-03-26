namespace Logs2Obs.QueryEngine.Alerts;

using System.Diagnostics;
using System.Diagnostics.Metrics;

public sealed class AlertEvaluationMetrics : IDisposable
{
    private readonly Meter _meter = new("logs2obs.alerts");
    private readonly Counter<long> _rulesEvaluated;
    private readonly Counter<long> _alertsFired;
    private readonly Counter<long> _alertsErrors;
    private readonly Histogram<double> _evaluationDurationMs;

    public AlertEvaluationMetrics()
    {
        _rulesEvaluated = _meter.CreateCounter<long>("logs2obs.alerts.rules_evaluated");
        _alertsFired = _meter.CreateCounter<long>("logs2obs.alerts.fired");
        _alertsErrors = _meter.CreateCounter<long>("logs2obs.alerts.errors");
        _evaluationDurationMs = _meter.CreateHistogram<double>("logs2obs.alerts.evaluation_duration_ms", unit: "ms");
    }

    public void RecordEvaluated(string tenantId, string ruleId) =>
        _rulesEvaluated.Add(1, new TagList { { "tenant_id", tenantId }, { "rule_id", ruleId } });

    public void RecordFired(string tenantId, string ruleId) =>
        _alertsFired.Add(1, new TagList { { "tenant_id", tenantId }, { "rule_id", ruleId } });

    public void RecordError(string tenantId, string ruleId, string reason) =>
        _alertsErrors.Add(1, new TagList { { "tenant_id", tenantId }, { "rule_id", ruleId }, { "reason", reason } });

    public void RecordDuration(string tenantId, string ruleId, double durationMs) =>
        _evaluationDurationMs.Record(durationMs, new TagList { { "tenant_id", tenantId }, { "rule_id", ruleId } });

    public void Dispose() => _meter.Dispose();
}
