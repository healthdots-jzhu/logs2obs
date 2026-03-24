using FluentAssertions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Storage;
using Logs2Obs.Core.Tests.Helpers;

namespace Logs2Obs.Core.Tests.Storage;

public class S3PathBuilderTests
{
    private static LogEntry MakeEntry(
        string tenantId = "t-test",
        LogType logType = LogType.Application,
        string environment = "production",
        DateTimeOffset? timestamp = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            SourceId = "payment-service",
            LogType = logType,
            Level = LogLevel.Info,
            Environment = environment,
            Category = "api",
            Timestamp = timestamp ?? new DateTimeOffset(2026, 3, 23, 14, 30, 0, TimeSpan.Zero),
            Message = "Test message",
            IngestedAt = DateTimeOffset.UtcNow,
            IngestionMode = IngestionMode.Push
        };

    // --- GetPartitionKey ---

    [Fact]
    public void GetPartitionKey_IncludesTenantPartitionSegment()
    {
        var entry = MakeEntry(tenantId: "acme-corp");

        var key = S3PathBuilder.GetPartitionKey(entry);

        key.Should().Contain("tenant=acme-corp");
    }

    [Fact]
    public void GetPartitionKey_IncludesLogTypePartitionSegment()
    {
        var entry = MakeEntry(logType: LogType.Error);

        var key = S3PathBuilder.GetPartitionKey(entry);

        key.Should().Contain("logtype=Error", Exactly.Once());
    }

    [Fact]
    public void GetPartitionKey_IncludesEnvironmentPartitionSegment()
    {
        var entry = MakeEntry(environment: "staging");

        var key = S3PathBuilder.GetPartitionKey(entry);

        key.Should().Contain("env=staging");
    }

    [Fact]
    public void GetPartitionKey_IncludesYearFromTimestamp()
    {
        var entry = MakeEntry(timestamp: new DateTimeOffset(2026, 3, 23, 14, 0, 0, TimeSpan.Zero));

        var key = S3PathBuilder.GetPartitionKey(entry);

        key.Should().Contain("year=2026");
    }

    [Fact]
    public void GetPartitionKey_IncludesMonthFromTimestamp()
    {
        var entry = MakeEntry(timestamp: new DateTimeOffset(2026, 3, 23, 14, 0, 0, TimeSpan.Zero));

        var key = S3PathBuilder.GetPartitionKey(entry);

        key.Should().Contain("month=03");
    }

    [Fact]
    public void GetPartitionKey_IncludesDayFromTimestamp()
    {
        var entry = MakeEntry(timestamp: new DateTimeOffset(2026, 3, 23, 14, 0, 0, TimeSpan.Zero));

        var key = S3PathBuilder.GetPartitionKey(entry);

        key.Should().Contain("day=23");
    }

    [Fact]
    public void GetPartitionKey_IncludesHourFromTimestamp()
    {
        var entry = MakeEntry(timestamp: new DateTimeOffset(2026, 3, 23, 14, 0, 0, TimeSpan.Zero));

        var key = S3PathBuilder.GetPartitionKey(entry);

        key.Should().Contain("hour=14");
    }

    [Fact]
    public void GetPartitionKey_ContainsAllSixHivePartitions()
    {
        var entry = MakeEntry();

        var key = S3PathBuilder.GetPartitionKey(entry);

        key.Should().Contain("tenant=");
        key.Should().Contain("logtype=");
        key.Should().Contain("env=");
        key.Should().Contain("year=");
        key.Should().Contain("month=");
        key.Should().Contain("day=");
        key.Should().Contain("hour=");
    }

    [Fact]
    public void GetPartitionKey_DoesNotContainFilenamePortion()
    {
        var entry = MakeEntry();

        var key = S3PathBuilder.GetPartitionKey(entry);

        key.Should().NotContain(".parquet");
        key.Should().NotContain("part-");
        key.Should().NotStartWith("logs/");
    }

    [Fact]
    public void GetPartitionKey_DifferentEntries_ProduceDifferentKeys()
    {
        var entry1 = MakeEntry(tenantId: "tenant-a", logType: LogType.Application);
        var entry2 = MakeEntry(tenantId: "tenant-b", logType: LogType.Error);

        var key1 = S3PathBuilder.GetPartitionKey(entry1);
        var key2 = S3PathBuilder.GetPartitionKey(entry2);

        key1.Should().NotBe(key2);
    }

    // --- Build ---

    [Fact]
    public void Build_ReturnsPathStartingWithLogsPrefix()
    {
        var entry = MakeEntry();
        var partitionKey = S3PathBuilder.GetPartitionKey(entry);

        var s3Key = S3PathBuilder.Build(partitionKey);

        s3Key.Should().StartWith("logs/");
    }

    [Fact]
    public void Build_ReturnsPathEndingWithParquetExtension()
    {
        var entry = MakeEntry();
        var partitionKey = S3PathBuilder.GetPartitionKey(entry);

        var s3Key = S3PathBuilder.Build(partitionKey);

        s3Key.Should().EndWith(".parquet");
    }

    [Fact]
    public void Build_ContainsPartitionKeyInPath()
    {
        var entry = MakeEntry();
        var partitionKey = S3PathBuilder.GetPartitionKey(entry);

        var s3Key = S3PathBuilder.Build(partitionKey);

        s3Key.Should().Contain(partitionKey);
    }

    [Fact]
    public void Build_GeneratesUniqueFilenamesOnEachCall()
    {
        var entry = MakeEntry();
        var partitionKey = S3PathBuilder.GetPartitionKey(entry);

        var key1 = S3PathBuilder.Build(partitionKey);
        var key2 = S3PathBuilder.Build(partitionKey);

        // Different UUID per call → different final file paths
        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Build_FilenameContainsPartPrefix()
    {
        var entry = MakeEntry();
        var partitionKey = S3PathBuilder.GetPartitionKey(entry);

        var s3Key = S3PathBuilder.Build(partitionKey);

        // Filename follows "part-{uuid7}.parquet" pattern
        s3Key.Should().Contain("part-");
    }

    [Fact]
    public void Build_FullRoundTrip_ProducesExpectedStructure()
    {
        var entry = MakeEntry(
            tenantId: "acme",
            logType: LogType.Error,
            environment: "production",
            timestamp: new DateTimeOffset(2026, 3, 23, 14, 0, 0, TimeSpan.Zero));
        var partitionKey = S3PathBuilder.GetPartitionKey(entry);

        var s3Key = S3PathBuilder.Build(partitionKey);

        s3Key.Should().StartWith("logs/");
        s3Key.Should().Contain("tenant=acme");
        s3Key.Should().Contain("logtype=Error");
        s3Key.Should().Contain("env=production");
        s3Key.Should().Contain("year=2026");
        s3Key.Should().Contain("month=03");
        s3Key.Should().Contain("day=23");
        s3Key.Should().Contain("hour=14");
        s3Key.Should().EndWith(".parquet");
    }
}
