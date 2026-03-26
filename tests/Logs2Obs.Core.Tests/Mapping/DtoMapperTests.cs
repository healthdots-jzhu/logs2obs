using Logs2Obs.Core.Mapping;

namespace Logs2Obs.Core.Tests.Mapping;

public class DtoMapperTests
{
    [Fact]
    public void ToDomain_WhenCalled_TenantIdIsAlwaysFromArgument()
    {
        var dto = CreateDto();

        var result = DtoMapper.ToDomain(dto, "tenant-expected", IngestionMode.Push);

        result.TenantId.Should().Be("tenant-expected");
    }

    [Fact]
    public void ToDomain_WhenCalled_IdIsSystemGenerated()
    {
        var dto = CreateDto();

        var result = DtoMapper.ToDomain(dto, "tenant-1", IngestionMode.Push);

        result.Id.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(result.Id, out _).Should().BeTrue();
    }

    [Fact]
    public void ToDomain_WhenCalled_IngestedAtIsSetToUtcNow()
    {
        var dto = CreateDto();
        var now = DateTimeOffset.UtcNow;

        var result = DtoMapper.ToDomain(dto, "tenant-1", IngestionMode.Push);

        result.IngestedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ToDomain_WithValidDto_MapsAllUserFields()
    {
        var dto = CreateDto(logType: "Error", message: "boom", sourceId: "api-gateway");

        var result = DtoMapper.ToDomain(dto, "tenant-1", IngestionMode.Push);

        result.SourceId.Should().Be("api-gateway");
        result.Message.Should().Be("boom");
        result.LogType.Should().Be(LogType.Error);
    }

    [Fact]
    public void ToDomain_WithNullTags_ReturnsEmptyTags()
    {
        var dto = CreateDto(tags: null);

        var result = DtoMapper.ToDomain(dto, "tenant-1", IngestionMode.Push);

        result.Tags.Should().NotBeNull();
        result.Tags.Should().BeEmpty();
    }

    [Fact]
    public void ToDomain_WithMetrics_MapsMetricsCorrectly()
    {
        var dto = CreateDto(
            logType: "Metric",
            metric: new MetricDto
            {
                MetricName = "http-latency",
                Unit = "ms",
                Value = 12.5,
                MetricType = "Histogram"
            });

        var result = DtoMapper.ToDomain(dto, "tenant-1", IngestionMode.Push);

        result.Metric.Should().NotBeNull();
        result.Metric!.MetricName.Should().Be("http-latency");
        result.Metric.Unit.Should().Be("ms");
        result.Metric.Value.Should().Be(12.5);
        result.Metric.MetricType.Should().Be(MetricType.Histogram);
    }

    private static LogEntryDto CreateDto(
        string sourceId = "payment-service",
        string logType = "Application",
        string level = "Information",
        string environment = "production",
        string message = "ok",
        IReadOnlyDictionary<string, string>? tags = null,
        MetricDto? metric = null) =>
        new()
        {
            SourceId = sourceId,
            LogType = logType,
            Level = level,
            Environment = environment,
            Category = "api",
            Timestamp = DateTimeOffset.UtcNow,
            Message = message,
            Tags = tags,
            Metric = metric
        };
}
