namespace Logs2Obs.Core.Abstractions;

using Logs2Obs.Core.Models;

/// <summary>Connector for pulling log entries from an external source on a schedule.</summary>
public interface IPullConnector
{
    /// <summary>Pulls log entries from the external source since the given timestamp.</summary>
    IAsyncEnumerable<LogEntry> PullAsync(PullJobConfig config, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Retrieves the persisted state for the given pull job, or null if no state exists.</summary>
    Task<IReadOnlyDictionary<string, string>?> GetStateAsync(string jobId, CancellationToken ct = default);

    /// <summary>Saves the state for the given pull job for use in the next run.</summary>
    Task SaveStateAsync(string jobId, IReadOnlyDictionary<string, string> state, CancellationToken ct = default);
}
