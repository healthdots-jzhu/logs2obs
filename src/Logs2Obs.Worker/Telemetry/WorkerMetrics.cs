namespace Logs2Obs.Worker.Telemetry;

using System.Diagnostics.Metrics;

public sealed class WorkerMetrics
{
    private readonly Meter _meter;

    public WorkerMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("Logs2Obs.Worker");
    }

    public Counter<long> IngestCounter => _meter.CreateCounter<long>("logs2obs.ingest.entries");
    public Counter<long> DuplicateCounter => _meter.CreateCounter<long>("logs2obs.ingest.duplicates");
    public Counter<long> RejectedCounter => _meter.CreateCounter<long>("logs2obs.ingest.rejected");
    public Histogram<double> ProcessingLatency => _meter.CreateHistogram<double>("logs2obs.worker.processing_ms", unit: "ms");
    public Counter<long> ParquetFilesWritten => _meter.CreateCounter<long>("logs2obs.parquet.files_written");
    public Counter<long> ParquetBytesWritten => _meter.CreateCounter<long>("logs2obs.parquet.bytes_written", unit: "bytes");
    public Counter<long> SearchIndexed => _meter.CreateCounter<long>("logs2obs.search.indexed");
    public Histogram<double> IndexLatency => _meter.CreateHistogram<double>("logs2obs.search.index_latency_ms", unit: "ms");
}
