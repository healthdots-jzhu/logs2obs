namespace Logs2Obs.Core.Abstractions;

using Logs2Obs.Core.Models;

/// <summary>Service for replaying/reprocessing log entries from object storage over a time range.</summary>
public interface IReplayService
{
    /// <summary>Starts a new replay job for the given tenant and time range.</summary>
    Task<ReplayJob> StartAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, ReplayOptions options, CancellationToken ct = default);

    /// <summary>Returns the current status of the specified replay job, or null if not found.</summary>
    Task<ReplayJob?> GetStatusAsync(string jobId, CancellationToken ct = default);

    /// <summary>Cancels the specified replay job if it is still running.</summary>
    Task CancelAsync(string jobId, CancellationToken ct = default);
}
