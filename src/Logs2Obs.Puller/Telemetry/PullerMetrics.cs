namespace Logs2Obs.Puller.Telemetry;

using System.Diagnostics.Metrics;

public sealed class PullerMetrics
{
    private readonly Counter<long> _entriesPulled;
    private readonly Counter<long> _batchesPublished;
    private readonly Counter<long> _jobErrors;
    private readonly Histogram<double> _pullDuration;

    public PullerMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("logs2obs.puller");
        _entriesPulled = meter.CreateCounter<long>("logs2obs.puller.entries_pulled");
        _batchesPublished = meter.CreateCounter<long>("logs2obs.puller.batches_published");
        _jobErrors = meter.CreateCounter<long>("logs2obs.puller.job_errors");
        _pullDuration = meter.CreateHistogram<double>("logs2obs.puller.pull_duration_ms", unit: "ms");
    }

    public void RecordEntriesPulled(int count, string tenantId, string connectorType) =>
        _entriesPulled.Add(count,
            [
                new("tenant_id", tenantId),
                new("connector_type", connectorType)
            ]);

    public void RecordBatchPublished(string tenantId) =>
        _batchesPublished.Add(1, [new("tenant_id", tenantId)]);

    public void RecordJobError(string tenantId, string connectorType, string errorType) =>
        _jobErrors.Add(1,
            [
                new("tenant_id", tenantId),
                new("connector_type", connectorType),
                new("error_type", errorType)
            ]);

    public void RecordPullDuration(double ms, string connectorType) =>
        _pullDuration.Record(ms, [new("connector_type", connectorType)]);
}
