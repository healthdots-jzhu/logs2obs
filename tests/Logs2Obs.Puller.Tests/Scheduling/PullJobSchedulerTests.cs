using Logs2Obs.Puller.Scheduling;
using Logs2Obs.Puller.Services;
using Logs2Obs.Puller.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Logs2Obs.Puller.Tests.Scheduling;

/// <summary>
/// Tests for Pull Job Scheduler using Quartz.NET.
/// Awaiting Dolores Phase 6 implementation.
/// </summary>
public class PullJobSchedulerTests
{
    [Fact]
    public async Task ScheduleJobAsync_RegistersQuartzJobWithCronTrigger()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(schedule: "0 */15 * * * ?");
        var scheduler = new Mock<Quartz.IScheduler>();
        scheduler.Setup(x => x.Start(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(x => x.CheckExists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        scheduler.Setup(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTimeOffset.UtcNow);

        var factory = new Mock<ISchedulerFactory>();
        factory.Setup(x => x.GetScheduler(It.IsAny<CancellationToken>())).ReturnsAsync(scheduler.Object);

        var metadataStore = new InMemoryMetadataStore();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var jobScheduler = new PullJobScheduler(factory.Object, metadataStore, serviceProvider, Mock.Of<ILogger<PullJobScheduler>>());
        SetScheduler(jobScheduler, scheduler.Object);

        // Act
        await jobScheduler.ScheduleJobAsync(config, CancellationToken.None);

        // Assert
        scheduler.Verify(x => x.ScheduleJob(
            It.Is<IJobDetail>(job => job.Key.Name == config.JobId),
            It.Is<ITrigger>(trigger => trigger is ICronTrigger && ((ICronTrigger)trigger).CronExpressionString == config.Schedule),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UnscheduleJobAsync_RemovesJobFromScheduler()
    {
        // Arrange
        var jobId = "job-to-remove";
        var scheduler = new Mock<Quartz.IScheduler>();
        scheduler.Setup(x => x.Start(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(x => x.DeleteJob(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        scheduler.Setup(x => x.CheckExists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var factory = new Mock<ISchedulerFactory>();
        factory.Setup(x => x.GetScheduler(It.IsAny<CancellationToken>())).ReturnsAsync(scheduler.Object);

        var metadataStore = new InMemoryMetadataStore();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var jobScheduler = new PullJobScheduler(factory.Object, metadataStore, serviceProvider, Mock.Of<ILogger<PullJobScheduler>>());
        SetScheduler(jobScheduler, scheduler.Object);

        // Act
        await jobScheduler.UnscheduleJobAsync(jobId, CancellationToken.None);

        // Assert
        scheduler.Verify(x => x.DeleteJob(It.Is<JobKey>(key => key.Name == jobId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnStartup_LoadsAndSchedulesAllEnabledJobs()
    {
        // Arrange
        var enabledJobs = new[]
        {
            TestDataBuilders.AValidPullJobConfig(jobId: "job-1", schedule: "0 * * * * ?"),
            TestDataBuilders.AValidPullJobConfig(jobId: "job-2", schedule: "0 */5 * * * ?")
        };

        var scheduler = new Mock<Quartz.IScheduler>();
        scheduler.Setup(x => x.Start(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        scheduler.Setup(x => x.CheckExists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        scheduler.Setup(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTimeOffset.UtcNow);

        var factory = new Mock<ISchedulerFactory>();
        factory.Setup(x => x.GetScheduler(It.IsAny<CancellationToken>())).ReturnsAsync(scheduler.Object);

        var metadataStore = new InMemoryMetadataStore();
        var stateService = new PullJobStateService(metadataStore);
        foreach (var job in enabledJobs)
        {
            await stateService.SaveJobAsync(job, CancellationToken.None);
        }

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var jobScheduler = new PullJobScheduler(factory.Object, metadataStore, serviceProvider, Mock.Of<ILogger<PullJobScheduler>>());

        // Act
        await InvokeExecuteAsync(jobScheduler, CancellationToken.None);

        // Assert
        scheduler.Verify(x => x.ScheduleJob(
            It.Is<IJobDetail>(job => enabledJobs.Any(cfg => cfg.JobId == job.Key.Name)),
            It.IsAny<ITrigger>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    private static void SetScheduler(PullJobScheduler scheduler, Quartz.IScheduler quartzScheduler)
    {
        var field = typeof(PullJobScheduler).GetField("_scheduler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(scheduler, quartzScheduler);
    }

    private static async Task InvokeExecuteAsync(PullJobScheduler scheduler, CancellationToken ct)
    {
        var method = typeof(PullJobScheduler).GetMethod("ExecuteAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var task = (Task)method!.Invoke(scheduler, new object?[] { ct })!;
        await task;
    }
}
