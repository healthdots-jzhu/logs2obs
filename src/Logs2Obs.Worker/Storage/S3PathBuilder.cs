namespace Logs2Obs.Worker.Storage;

using Logs2Obs.Core.Models;

public static class S3PathBuilder
{
    public static string GetPartitionKey(LogEntry entry)
    {
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampUnixMs);
        return $"{entry.TenantId}/{timestamp:yyyy/MM/dd/HH}";
    }

    public static string BuildPath(LogEntry entry)
    {
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampUnixMs);
        var batchId = Guid.CreateVersion7().ToString("N");
        return $"logs/{entry.TenantId}/{timestamp:yyyy/MM/dd/HH}/{batchId}.parquet";
    }
}