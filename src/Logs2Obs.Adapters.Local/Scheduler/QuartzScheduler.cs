namespace Logs2Obs.Adapters.Local.Scheduler;

using Logs2Obs.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzIScheduler = Quartz.IScheduler;

public sealed class QuartzScheduler(
    ISchedulerFactory schedulerFactory,
    ILogger<QuartzScheduler> logger) : Logs2Obs.Core.Abstractions.IScheduler
{
    private QuartzIScheduler? _quartzScheduler;

    private async Task<QuartzIScheduler> GetSchedulerAsync(CancellationToken ct)
    {
        if (_quartzScheduler is null)
        {
            _quartzScheduler = await schedulerFactory.GetScheduler(ct);
            if (!_quartzScheduler.IsStarted)
                await _quartzScheduler.Start(ct);
        }
        return _quartzScheduler;
    }

    public async Task ScheduleAsync(string jobId, string cronExpression, string jobType, CancellationToken ct = default)
    {
        var scheduler = await GetSchedulerAsync(ct);

        var job = JobBuilder.Create<LogsJobDispatcher>()
            .WithIdentity(jobId, "logs2obs")
            .UsingJobData("jobType", jobType)
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobId}-trigger", "logs2obs")
            .WithCronSchedule(cronExpression)
            .ForJob(job)
            .Build();

        await scheduler.ScheduleJob(job, trigger, ct);
        logger.LogInformation("Scheduled job {JobId} ({JobType}) with cron {Cron}", jobId, jobType, cronExpression);
    }

    public async Task UnscheduleAsync(string jobId, CancellationToken ct = default)
    {
        var scheduler = await GetSchedulerAsync(ct);
        var triggerKey = new TriggerKey($"{jobId}-trigger", "logs2obs");
        await scheduler.UnscheduleJob(triggerKey, ct);
        logger.LogInformation("Unscheduled job {JobId}", jobId);
    }

    public async Task<DateTimeOffset?> GetNextRunTimeAsync(string jobId, CancellationToken ct = default)
    {
        var scheduler = await GetSchedulerAsync(ct);
        var triggerKey = new TriggerKey($"{jobId}-trigger", "logs2obs");
        var trigger = await scheduler.GetTrigger(triggerKey, ct);
        var nextFireTime = trigger?.GetNextFireTimeUtc();
        return nextFireTime.HasValue ? new DateTimeOffset(nextFireTime.Value.UtcDateTime) : null;
    }
}

[DisallowConcurrentExecution]
internal sealed class LogsJobDispatcher : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        // Actual dispatch happens in the Worker project (Phase 5). This is a stub.
        return Task.CompletedTask;
    }
}
