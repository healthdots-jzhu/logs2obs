namespace Logs2Obs.QueryEngine.Models;

using Logs2Obs.Core.Models;

public sealed record LogEntryBatch(
    IReadOnlyList<LogEntry> Entries,
    string TenantId,
    string BatchId);
