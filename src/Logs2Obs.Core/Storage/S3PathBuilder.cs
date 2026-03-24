namespace Logs2Obs.Core.Storage;

/// <summary>Builds deterministic S3/object-store paths for log files and audit records.</summary>
public static class S3PathBuilder
{
    /// <summary>Builds the S3 key for a Parquet log batch file.</summary>
    public static string BuildLogPath(string tenantId, DateTimeOffset timestamp, string batchId)
        => $"{tenantId}/{timestamp:yyyy}/{timestamp:MM}/{timestamp:dd}/{timestamp:HH}/{batchId}.parquet";

    /// <summary>Builds the S3 key for an AI query audit record.</summary>
    public static string BuildAiAuditPath(string tenantId, string queryId)
    {
        var now = DateTimeOffset.UtcNow;
        return $"ai-audit/{tenantId}/{now:yyyy}/{now:MM}/{now:dd}/{queryId}.json";
    }
}
