using Logs2Obs.Core.Storage;

namespace Logs2Obs.Core.Tests.Storage;

public class S3PathBuilderTests
{
    [Fact]
    public void Build_WithKnownInput_ReturnsExpectedPath()
    {
        var timestamp = new DateTimeOffset(2026, 3, 23, 14, 0, 0, TimeSpan.Zero);

        var result = S3PathBuilder.BuildLogPath("tenant-1", timestamp, "batch-123");

        result.Should().Be("tenant-1/2026/03/23/14/batch-123.parquet");
    }

    [Fact]
    public void Build_PathContainsTenantId()
    {
        var timestamp = new DateTimeOffset(2026, 3, 23, 14, 0, 0, TimeSpan.Zero);

        var result = S3PathBuilder.BuildLogPath("tenant-1", timestamp, "batch-123");

        result.Should().Contain("tenant-1");
    }

    [Fact]
    public void Build_PathContainsSource()
    {
        var timestamp = new DateTimeOffset(2026, 3, 23, 14, 0, 0, TimeSpan.Zero);

        var result = S3PathBuilder.BuildLogPath("tenant-1", timestamp, "source-a");

        result.Should().Contain("source-a");
    }

    [Fact]
    public void Build_PathEndsWithParquetExtension()
    {
        var timestamp = new DateTimeOffset(2026, 3, 23, 14, 0, 0, TimeSpan.Zero);

        var result = S3PathBuilder.BuildLogPath("tenant-1", timestamp, "batch-123");

        result.Should().EndWith(".parquet");
    }

    [Fact]
    public void Build_ForSameInputTwice_ReturnsDifferentPaths()
    {
        var timestamp = new DateTimeOffset(2026, 3, 23, 14, 0, 0, TimeSpan.Zero);
        var tenantId = "tenant-1";

        var result1 = S3PathBuilder.BuildLogPath(tenantId, timestamp, Guid.NewGuid().ToString("N"));
        var result2 = S3PathBuilder.BuildLogPath(tenantId, timestamp, Guid.NewGuid().ToString("N"));

        result1.Should().NotBe(result2);
    }
}
