using Logs2Obs.Worker.Models;

namespace Logs2Obs.Worker.Tests.Helpers;

public static class TestDataBuilders
{
    public static LogEntry AValidLogEntry(string? tenantId = null, LogLevel? level = null)
    {
        return new LogEntry
        {
            Id = Guid.NewGuid().ToString(),
            SourceId = "test-source",
            LogType = LogType.Application,
            Level = level ?? LogLevel.Information,
            Environment = "test",
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Message = "Test log message",
            TenantId = tenantId ?? "test-tenant",
            IngestedAt = DateTimeOffset.UtcNow,
            IngestionMode = IngestionMode.Push,
            Tags = new Dictionary<string, string>
            {
                ["test-key"] = "test-value"
            }
        };
    }

    public static MessageEnvelope<T> AValidMessageEnvelope<T>(T payload, string? receiptHandle = null)
    {
        return new MessageEnvelope<T>
        {
            Payload = payload,
            ReceiptHandle = receiptHandle ?? Guid.NewGuid().ToString(),
            EnqueuedAt = DateTimeOffset.UtcNow
        };
    }

    public static LogEntryBatch AValidLogEntryBatch(int count = 5, string? tenantId = null)
    {
        var entries = new List<LogEntry>();
        for (int i = 0; i < count; i++)
        {
            entries.Add(AValidLogEntry(tenantId));
        }
        return new LogEntryBatch(
            Entries: entries,
            TenantId: tenantId ?? "test-tenant",
            BatchId: Guid.NewGuid().ToString());
    }
}
