namespace Logs2Obs.QueryEngine.Tests.Telemetry;

using Logs2Obs.QueryEngine.Telemetry;

public class QueryEngineMetricsTests
{
    [Fact]
    public void RecordQuerySubmitted_IncrementsCounter()
    {
        using var metrics = new QueryEngineMetrics();

        Action act = () => metrics.RecordSubmitted("tenant-1", QueryTier.Hot);

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordQueryCompleted_IncrementsCounter()
    {
        using var metrics = new QueryEngineMetrics();

        Action act = () => metrics.RecordCompleted("tenant-1", QueryTier.Warm);

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordQueryRejected_IncrementsCounter()
    {
        using var metrics = new QueryEngineMetrics();

        Action act = () => metrics.RecordRejected("tenant-1", "scan_limit");

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordQueryDuration_RecordsHistogram()
    {
        using var metrics = new QueryEngineMetrics();

        Action act = () => metrics.RecordDuration(QueryTier.Hot, 123.45);

        act.Should().NotThrow();
    }
}
