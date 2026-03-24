using FluentAssertions;
using FluentValidation.TestHelper;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Tests.Helpers;
using Logs2Obs.Core.Validation;

namespace Logs2Obs.Core.Tests.Validation;

public class LogEntryDtoValidatorTests
{
    private readonly LogEntryDtoValidator _sut = new();

    [Fact]
    public void Validate_ValidDto_PassesValidation()
    {
        var result = _sut.TestValidate(TestDataBuilders.AValidLogEntryDto());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptySourceId_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.SourceId = string.Empty;

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.SourceId);
    }

    [Theory]
    [InlineData("source id!")]
    [InlineData("source id with spaces")]
    [InlineData("source@id")]
    [InlineData("source#id")]
    public void Validate_SourceIdWithSpecialChars_Fails(string sourceId)
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.SourceId = sourceId;

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.SourceId);
    }

    [Theory]
    [InlineData("payment-service")]
    [InlineData("payment_service")]
    [InlineData("payment.service")]
    [InlineData("payment:service")]
    [InlineData("payment/service")]
    [InlineData("PaymentService123")]
    public void Validate_SourceIdWithAllowedChars_Passes(string sourceId)
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.SourceId = sourceId;

        var result = _sut.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.SourceId);
    }

    [Fact]
    public void Validate_InvalidLogType_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.LogType = "NotAType";

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.LogType);
    }

    [Theory]
    [InlineData("Application")]
    [InlineData("Error")]
    [InlineData("Network")]
    [InlineData("OS")]
    [InlineData("Metric")]
    [InlineData("Audit")]
    [InlineData("Custom")]
    [InlineData("application")]   // case-insensitive
    [InlineData("ERROR")]
    public void Validate_ValidLogType_Passes(string logType)
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.LogType = logType;
        if (logType.Equals("Metric", StringComparison.OrdinalIgnoreCase))
            dto.Metric = new MetricPayloadDto { MetricName = "test", Unit = "ms", Value = 1.0 };

        var result = _sut.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.LogType);
    }

    [Fact]
    public void Validate_InvalidLevel_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Level = "NotALevel";

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Level);
    }

    [Theory]
    [InlineData("Trace")]
    [InlineData("Debug")]
    [InlineData("Info")]
    [InlineData("Warn")]
    [InlineData("Error")]
    [InlineData("Fatal")]
    [InlineData("info")]
    [InlineData("ERROR")]
    public void Validate_ValidLevel_Passes(string level)
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Level = level;

        var result = _sut.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.Level);
    }

    [Fact]
    public void Validate_TimestampMoreThan5MinutesInFuture_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Timestamp = DateTimeOffset.UtcNow.AddMinutes(6);

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Timestamp);
    }

    [Fact]
    public void Validate_TimestampMoreThan30DaysInPast_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Timestamp = DateTimeOffset.UtcNow.AddDays(-31);

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Timestamp);
    }

    [Fact]
    public void Validate_TimestampExactly5MinutesInFuture_Passes()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Timestamp = DateTimeOffset.UtcNow.AddMinutes(4).AddSeconds(30);

        var result = _sut.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.Timestamp);
    }

    [Fact]
    public void Validate_EmptyMessage_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Message = string.Empty;

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Message);
    }

    [Fact]
    public void Validate_MessageExceeds65536Chars_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Message = new string('x', 65537);

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Message);
    }

    [Fact]
    public void Validate_MessageExactly65536Chars_Passes()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Message = new string('x', 65536);

        var result = _sut.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.Message);
    }

    [Fact]
    public void Validate_TagsWithMoreThan50Entries_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Tags = Enumerable.Range(1, 51).ToDictionary(i => $"key{i}", i => $"value{i}");

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Tags);
    }

    [Fact]
    public void Validate_TagsWithExactly50Entries_Passes()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Tags = Enumerable.Range(1, 50).ToDictionary(i => $"key{i}", i => $"value{i}");

        var result = _sut.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.Tags);
    }

    [Fact]
    public void Validate_TagKeyExceeds128Chars_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Tags = new Dictionary<string, string>
        {
            [new string('k', 129)] = "value"
        };

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Tags);
    }

    [Fact]
    public void Validate_TagValueExceeds1024Chars_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Tags = new Dictionary<string, string>
        {
            ["valid-key"] = new string('v', 1025)
        };

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Tags);
    }

    [Fact]
    public void Validate_MetricLogTypeWithoutMetricPayload_Fails()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.LogType = "Metric";
        dto.Metric = null;

        var result = _sut.TestValidate(dto);

        result.ShouldHaveValidationErrorFor(x => x.Metric);
    }

    [Fact]
    public void Validate_MetricLogTypeWithMetricPayload_Passes()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.LogType = "Metric";
        dto.Metric = new MetricPayloadDto
        {
            MetricName = "http-latency",
            Unit = "ms",
            Value = 120.5
        };

        var result = _sut.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.Metric);
    }

    [Fact]
    public void Validate_NullTags_Passes()
    {
        var dto = TestDataBuilders.AValidLogEntryDto();
        dto.Tags = null;

        var result = _sut.TestValidate(dto);

        result.ShouldNotHaveValidationErrorFor(x => x.Tags);
    }
}
