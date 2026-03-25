namespace Logs2Obs.Puller.Tests.Helpers;

using System.Text;
using System.Text.Json;

public static class TestDataBuilders
{
    public static PullJobConfig AValidPullJobConfig(
        string jobId = "test-job-1",
        string tenantId = "tenant-abc",
        ConnectorType connectorType = ConnectorType.Http,
        string schedule = "0 * * * * ?") => new()
    {
        JobId = jobId,
        TenantId = tenantId,
        ConnectorType = connectorType,
        Schedule = schedule,
        ConnectorConfig = new Dictionary<string, string> { ["url"] = "https://example.com/logs" },
        IsEnabled = true
    };

    public static LogEntry AValidLogEntry(string tenantId = "tenant-abc") => new()
    {
        Id = Guid.NewGuid().ToString(),
        SourceId = "test-source",
        LogType = LogType.Application,
        Level = LogLevel.Information,
        Environment = "test",
        Category = "TestCategory",
        TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Message = "Test log entry",
        TraceId = Guid.NewGuid().ToString(),
        TenantId = tenantId,
        IngestedAt = DateTimeOffset.UtcNow,
        IngestionMode = IngestionMode.Pull
    };

    public static MemoryStream AValidNdjsonStream(IEnumerable<LogEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.AppendLine(JsonSerializer.Serialize(entry));
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
