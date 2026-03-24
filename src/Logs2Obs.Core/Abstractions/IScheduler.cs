namespace Logs2Obs.Core.Abstractions;

/// <summary>Cloud-agnostic job scheduler abstraction for cron-based scheduling.</summary>
public interface IScheduler
{
    /// <summary>Schedules a job with the given cron expression and job type identifier.</summary>
    Task ScheduleAsync(string jobId, string cronExpression, string jobType, CancellationToken ct = default);

    /// <summary>Removes the schedule for the given job.</summary>
    Task UnscheduleAsync(string jobId, CancellationToken ct = default);

    /// <summary>Returns the next scheduled run time for the given job, or null if not scheduled.</summary>
    Task<DateTimeOffset?> GetNextRunTimeAsync(string jobId, CancellationToken ct = default);
}
