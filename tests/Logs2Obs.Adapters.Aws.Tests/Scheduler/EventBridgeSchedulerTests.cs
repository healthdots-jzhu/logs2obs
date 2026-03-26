namespace Logs2Obs.Adapters.Aws.Tests.Scheduler;

using Logs2Obs.Adapters.Aws.Scheduler;

public sealed class EventBridgeSchedulerTests
{
    private readonly Type _sutType = typeof(EventBridgeScheduler);

    [Fact(Skip = "Requires AWS EventBridge Scheduler API.")]
    public void ScheduleAsync_WhenCronValid_CreatesSchedule()
    {
        _ = _sutType;
    }

    [Fact(Skip = "Requires AWS EventBridge Scheduler API.")]
    public void UnscheduleAsync_WhenJobExists_RemovesSchedule()
    {
        _ = _sutType;
    }
}
