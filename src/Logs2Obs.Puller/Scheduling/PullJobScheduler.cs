namespace Logs2Obs.Puller.Scheduling;

using System.Runtime.CompilerServices;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Puller.Services;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzIScheduler = Quartz.IScheduler;

public sealed class PullJobScheduler : BackgroundService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IMetadataStore _metadataStore;
    private readonly ILogger<PullJobScheduler> _logger;
    private QuartzIScheduler? _scheduler;

    public PullJobScheduler(
        ISchedulerFactory schedulerFactory,
        IMetadataStore metadataStore,
        IServiceProvider serviceProvider,
        ILogger<PullJobScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _schedulerFactory = schedulerFactory;
        _metadataStore = metadataStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _scheduler = await _schedulerFactory.GetScheduler(stoppingToken);
        await _scheduler.Start(stoppingToken);

        await foreach (var job in LoadEnabledJobsAsync(stoppingToken))
        {
            await ScheduleJobAsync(job, stoppingToken);
        }
    }

    private async IAsyncEnumerable<PullJobConfig> LoadEnabledJobsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var record in _metadataStore.QueryAsync<PullJobStateRecord>("pulljob", r => r.Config.IsEnabled, ct))
        {
            yield return record.Config;
        }
    }

    public async Task ScheduleJobAsync(PullJobConfig config, CancellationToken ct = default)
    {
        if (_scheduler == null) throw new InvalidOperationException("Scheduler not initialized");

        var jobKey = new JobKey(config.JobId);

        var job = JobBuilder.Create<PullJobQuartzJob>()
            .WithIdentity(jobKey)
            .UsingJobData(new JobDataMap { ["config"] = config })
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{config.JobId}-trigger")
            .WithCronSchedule(config.Schedule)
            .Build();

        if (await _scheduler.CheckExists(jobKey, ct))
        {
            await _scheduler.DeleteJob(jobKey, ct);
        }

        await _scheduler.ScheduleJob(job, trigger, ct);
        _logger.LogInformation("Scheduled pull job {JobId} for tenant {TenantId}.", config.JobId, config.TenantId);
    }

    public async Task UnscheduleJobAsync(string jobId, CancellationToken ct = default)
    {
        if (_scheduler == null) throw new InvalidOperationException("Scheduler not initialized");

        var jobKey = new JobKey(jobId);
        await _scheduler.DeleteJob(jobKey, ct);
        _logger.LogInformation("Unscheduled pull job {JobId}.", jobId);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_scheduler != null)
        {
            await _scheduler.Shutdown(cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }
}
