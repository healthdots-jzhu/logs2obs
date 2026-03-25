using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Logs2Obs.Worker.Telemetry;

namespace Logs2Obs.Worker.Tests.Telemetry;

public class WorkerMetricsTests : IDisposable
{
    private readonly Meter _meter;
    private readonly WorkerMetrics _metrics;
    private readonly MeterListener _listener;

    public WorkerMetricsTests()
    {
        _meter = new Meter("Logs2Obs.Worker.Test." + Guid.NewGuid());
        var mockMeterFactory = new Mock<IMeterFactory>();
        mockMeterFactory.Setup(f => f.Create(It.IsAny<MeterOptions>())).Returns(_meter);
        _metrics = new WorkerMetrics(mockMeterFactory.Object);
        _listener = new MeterListener();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
    }

    [Fact]
    public void IngestCounter_WhenIncremented_ReflectsCorrectValue()
    {
        long total = 0;
        _listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "logs2obs.ingest.entries")
                l.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref total, measurement));
        _listener.Start();

        _metrics.IngestCounter.Add(10);
        _metrics.IngestCounter.Add(5);

        total.Should().Be(15, "Ingest counter should sum all increments");
    }

    [Fact]
    public void DuplicateCounter_WhenIncremented_ReflectsCorrectValue()
    {
        long total = 0;
        _listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "logs2obs.ingest.duplicates")
                l.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref total, measurement));
        _listener.Start();

        _metrics.DuplicateCounter.Add(3, new KeyValuePair<string, object?>("tenant_id", "t1"));

        total.Should().Be(3, "Duplicate counter should track rejected entries");
    }

    [Fact]
    public void ProcessingLatency_WhenRecorded_HistogramUpdated()
    {
        int count = 0;
        _listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "logs2obs.worker.processing_ms")
                l.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<double>((_, _, _, _) =>
            Interlocked.Increment(ref count));
        _listener.Start();

        _metrics.ProcessingLatency.Record(250.0);
        _metrics.ProcessingLatency.Record(300.0);

        count.Should().Be(2, "Histogram should record both observations");
    }

    [Fact]
    public void Metrics_WithTenantIdTag_GroupedCorrectly()
    {
        var totals = new ConcurrentDictionary<string, long>();
        _listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "logs2obs.ingest.entries")
                l.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "tenant_id")
                    totals.AddOrUpdate(tag.Value?.ToString() ?? "", measurement, (_, v) => v + measurement);
            }
        });
        _listener.Start();

        _metrics.IngestCounter.Add(10, new KeyValuePair<string, object?>("tenant_id", "tenant-1"));
        _metrics.IngestCounter.Add(5, new KeyValuePair<string, object?>("tenant_id", "tenant-2"));

        totals["tenant-1"].Should().Be(10);
        totals["tenant-2"].Should().Be(5);
    }

    [Fact]
    public void FlushCounter_WhenParquetFlushed_IncrementsByOne()
    {
        long total = 0;
        _listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "logs2obs.parquet.files_written")
                l.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref total, measurement));
        _listener.Start();

        _metrics.ParquetFilesWritten.Add(1);

        total.Should().Be(1, "Flush counter should increment once per Parquet flush");
    }

    [Fact]
    public void ErrorCounter_WhenObjectStoreFails_IncrementsByOne()
    {
        long total = 0;
        _listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "logs2obs.ingest.rejected")
                l.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref total, measurement));
        _listener.Start();

        _metrics.RejectedCounter.Add(1, new KeyValuePair<string, object?>("tenant_id", "t1"));

        total.Should().Be(1, "Error counter should track failures by type");
    }
}
