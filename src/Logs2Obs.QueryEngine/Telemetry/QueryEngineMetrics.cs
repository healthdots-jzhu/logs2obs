namespace Logs2Obs.QueryEngine.Telemetry;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Logs2Obs.Core.Models;

public sealed class QueryEngineMetrics : IDisposable
{
    private readonly Meter _meter = new("logs2obs.queryengine");
    private readonly Counter<long> _queriesSubmitted;
    private readonly Counter<long> _queriesCompleted;
    private readonly Counter<long> _queriesRejected;
    private readonly Counter<long> _costConfirmationsRequired;
    private readonly Histogram<double> _queryDurationMs;

    public QueryEngineMetrics()
    {
        _queriesSubmitted = _meter.CreateCounter<long>("logs2obs.queryengine.queries_submitted");
        _queriesCompleted = _meter.CreateCounter<long>("logs2obs.queryengine.queries_completed");
        _queriesRejected = _meter.CreateCounter<long>("logs2obs.queryengine.queries_rejected");
        _costConfirmationsRequired = _meter.CreateCounter<long>("logs2obs.queryengine.cost_confirmations_required");
        _queryDurationMs = _meter.CreateHistogram<double>("logs2obs.queryengine.query_duration_ms");
    }

    public void RecordSubmitted(string tenantId, QueryTier tier) =>
        _queriesSubmitted.Add(1, new TagList { { "tenant_id", tenantId }, { "tier", tier.ToString() } });

    public void RecordCompleted(string tenantId, QueryTier tier) =>
        _queriesCompleted.Add(1, new TagList { { "tenant_id", tenantId }, { "tier", tier.ToString() } });

    public void RecordRejected(string tenantId, string reason) =>
        _queriesRejected.Add(1, new TagList { { "tenant_id", tenantId }, { "reason", reason } });

    public void RecordCostConfirmationRequired(string tenantId) =>
        _costConfirmationsRequired.Add(1, new TagList { { "tenant_id", tenantId } });

    public void RecordDuration(QueryTier tier, double durationMs) =>
        _queryDurationMs.Record(durationMs, new TagList { { "tier", tier.ToString() } });

    public void Dispose()
    {
        _meter.Dispose();
    }
}
