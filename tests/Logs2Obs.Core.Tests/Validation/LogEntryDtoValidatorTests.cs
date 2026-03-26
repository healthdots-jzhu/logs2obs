using Logs2Obs.Core.Validation;

namespace Logs2Obs.Core.Tests.Validation;

public class LogEntryDtoValidatorTests
{
    private readonly LogEntryDtoValidator _validator = new();

    [Fact]
    public void Validate_WithValidDto_ReturnsValid()
    {
        var dto = CreateValidDto();

        var result = _validator.Validate(dto);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenMessageIsEmpty_ReturnsInvalid()
    {
        var dto = CreateValidDto(message: string.Empty);

        var result = _validator.Validate(dto);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WhenMessageExceedsMaxLength_ReturnsInvalid()
    {
        var dto = CreateValidDto(message: new string('x', 65537));

        var result = _validator.Validate(dto);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WhenSourceIsEmpty_ReturnsInvalid()
    {
        var dto = CreateValidDto(sourceId: string.Empty);

        var result = _validator.Validate(dto);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WhenTimestampIsMinValue_ReturnsInvalid()
    {
        var dto = CreateValidDto(timestamp: DateTimeOffset.MinValue);

        var result = _validator.Validate(dto);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithValidMetrics_ReturnsValid()
    {
        var dto = CreateValidDto(logType: "Metric", metric: new MetricDto
        {
            MetricName = "http-latency",
            Unit = "ms",
            Value = 12.5,
            MetricType = "Histogram"
        });

        var result = _validator.Validate(dto);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenMetricNameIsEmpty_ReturnsInvalid()
    {
        var dto = CreateValidDto(logType: "Metric", metric: new MetricDto
        {
            MetricName = string.Empty,
            Unit = "ms",
            Value = 12.5,
            MetricType = "Histogram"
        });

        var result = _validator.Validate(dto);

        result.IsValid.Should().BeFalse();
    }

    private static LogEntryDto CreateValidDto(
        string sourceId = "payment-service",
        string logType = "Application",
        string message = "ok",
        DateTimeOffset? timestamp = null,
        MetricDto? metric = null) =>
        new()
        {
            SourceId = sourceId,
            LogType = logType,
            Level = "Information",
            Environment = "production",
            Category = "api",
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Message = message,
            Metric = metric
        };
}
