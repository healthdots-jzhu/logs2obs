using Logs2Obs.Core.Schema;

namespace Logs2Obs.Core.Tests.Schema;

public class SchemaInferenceEngineTests
{
    private static readonly string[] ExpectedExtraFields = ["region", "role"];

    [Fact]
    public void Infer_FromLogEntries_DetectsStringField()
    {
        var entries = new[]
        {
            CreateEntry(new Dictionary<string, string> { ["region"] = "us-east" })
        };

        var result = SchemaInferenceEngine.InferSchema(entries);

        result.Should().ContainSingle(field => field.Name == "region" && field.InferredType == "string");
    }

    [Fact]
    public void Infer_FromLogEntries_DetectsNumericField()
    {
        var entries = new[]
        {
            CreateEntry(new Dictionary<string, string> { ["latency"] = "123" }),
            CreateEntry(new Dictionary<string, string> { ["latency"] = "456" })
        };

        var result = SchemaInferenceEngine.InferSchema(entries);

        result.Should().ContainSingle(field => field.Name == "latency" && field.InferredType == "int64");
    }

    [Fact]
    public void Infer_FromEmptyEntries_ReturnsEmptySchema()
    {
        var result = SchemaInferenceEngine.InferSchema(Array.Empty<LogEntry>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void Infer_FromMixedTypes_ChoosesWidestType()
    {
        var entries = new[]
        {
            CreateEntry(new Dictionary<string, string> { ["duration"] = "1" }),
            CreateEntry(new Dictionary<string, string> { ["duration"] = "1.5" })
        };

        var result = SchemaInferenceEngine.InferSchema(entries);

        result.Should().ContainSingle(field => field.Name == "duration" && field.InferredType == "double");
    }

    [Fact]
    public void Infer_WhenExtraFieldsPresent_IncludesAllFields()
    {
        var entries = new[]
        {
            CreateEntry(new Dictionary<string, string> { ["region"] = "us-east", ["role"] = "api" })
        };

        var result = SchemaInferenceEngine.InferSchema(entries);

        result.Select(field => field.Name).Should().BeEquivalentTo(ExpectedExtraFields);
    }

    private static LogEntry CreateEntry(IReadOnlyDictionary<string, string>? tags) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceId = "payment-service",
            LogType = LogType.Application,
            Level = LogLevel.Information,
            Environment = "production",
            Category = "api",
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Message = "ok",
            Tags = tags,
            TenantId = "tenant-1",
            IngestedAt = DateTimeOffset.UtcNow,
            IngestionMode = IngestionMode.Push,
            SchemaVersion = 1
        };
}
