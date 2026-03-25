using Logs2Obs.Worker.Parquet;
using Logs2Obs.Worker.Tests.Helpers;
using Parquet.Serialization;
using System.Text.Json;

namespace Logs2Obs.Worker.Tests.Parquet;

// Mirror of the private LogEntryParquetRecord. Strings must be nullable because
// Parquet.Net serialises string columns as nullable (String?) by default.
file sealed class ParquetRow
{
    public string? Id { get; set; }
    public string? SourceId { get; set; }
    public string? LogType { get; set; }
    public string? Level { get; set; }
    public string? Environment { get; set; }
    public string? Category { get; set; }
    public long TimestampUnixMs { get; set; }
    public string? Message { get; set; }
    public string? TraceId { get; set; }
    public string? TenantId { get; set; }
    public int SchemaVersion { get; set; }
    public string? Tags { get; set; }
}

public class ParquetWriterTests
{
    [Fact]
    public async Task WriteAsync_WithValidEntries_ReturnsNonEmptyStream()
    {
        var entries = new List<LogEntry>
        {
            TestDataBuilders.AValidLogEntry(),
            TestDataBuilders.AValidLogEntry(),
            TestDataBuilders.AValidLogEntry()
        };
        var writer = new ParquetWriter();

        var stream = await writer.WriteAsync(entries, CancellationToken.None);

        stream.Should().NotBeNull("Parquet writer should return a stream");
        stream.Length.Should().BeGreaterThan(0, "Parquet file should have content");
        stream.Position.Should().Be(0, "Stream should be positioned at start for reading");
    }

    [Fact]
    public async Task WriteAsync_WithEmptyList_ReturnsEmptyParquetStream()
    {
        var entries = new List<LogEntry>();
        var writer = new ParquetWriter();

        var stream = await writer.WriteAsync(entries, CancellationToken.None);

        stream.Should().NotBeNull("Parquet writer should return a stream even for empty input");
        stream.Length.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task WriteAsync_AllRequiredFieldsPresent_InOutputSchema()
    {
        var entry = TestDataBuilders.AValidLogEntry();
        var writer = new ParquetWriter();

        var stream = await writer.WriteAsync(new List<LogEntry> { entry }, CancellationToken.None);
        var rows = await ParquetSerializer.DeserializeAsync<ParquetRow>(stream);

        rows.Should().HaveCount(1);
        var row = rows[0];
        row.Id.Should().Be(entry.Id);
        row.TenantId.Should().Be(entry.TenantId);
        row.TimestampUnixMs.Should().Be(entry.TimestampUnixMs);
        row.Level.Should().Be(entry.Level.ToString());
        row.Message.Should().Be(entry.Message);
        row.SourceId.Should().Be(entry.SourceId);
    }

    [Fact]
    public async Task WriteAsync_TagsSerializedAsJson()
    {
        var entry = TestDataBuilders.AValidLogEntry();
        var writer = new ParquetWriter();

        var stream = await writer.WriteAsync(new List<LogEntry> { entry }, CancellationToken.None);
        var rows = await ParquetSerializer.DeserializeAsync<ParquetRow>(stream);

        rows.Should().HaveCount(1);
        var tagsJson = rows[0].Tags;
        tagsJson.Should().NotBeNullOrEmpty("Tags should be serialized as JSON");

        var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(tagsJson!);
        tags.Should().NotBeNull();
        tags!.Should().ContainKey("test-key");
        tags["test-key"].Should().Be("test-value");
    }
}
