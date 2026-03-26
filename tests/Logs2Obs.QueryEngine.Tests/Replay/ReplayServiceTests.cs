namespace Logs2Obs.QueryEngine.Tests.Replay;

using Logs2Obs.QueryEngine.Options;
using Logs2Obs.QueryEngine.Replay;
using Microsoft.Extensions.Options;

public class ReplayServiceTests
{
    private const string TenantId = "tenant-1";

    [Fact]
    public async Task StartAsync_CreatesJobWithQueuedStatus()
    {
        var metadataStore = new Mock<IMetadataStore>();
        ReplayJob? capturedJob = null;
        metadataStore.Setup(x => x.PutAsync("replay-jobs", It.IsAny<ReplayJob>(), It.IsAny<CancellationToken>()))
            .Callback<string, ReplayJob, CancellationToken>((_, job, _) => capturedJob = job)
            .Returns(Task.CompletedTask);

        var service = CreateService(metadataStore);

        var job = await service.StartAsync(TenantId, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, new ReplayOptions(), CancellationToken.None);

        job.Status.Should().Be(ReplayStatus.Queued);
        capturedJob.Should().NotBeNull();
        capturedJob!.Status.Should().Be(ReplayStatus.Queued);
    }

    [Fact]
    public async Task StartAsync_PersistsJobToMetadataStore()
    {
        var metadataStore = new Mock<IMetadataStore>();
        metadataStore.Setup(x => x.PutAsync("replay-jobs", It.IsAny<ReplayJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(metadataStore);

        await service.StartAsync(TenantId, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow, new ReplayOptions(), CancellationToken.None);

        metadataStore.Verify(x => x.PutAsync("replay-jobs", It.IsAny<ReplayJob>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_PublishesReplayStartedEvent()
    {
        var metadataStore = new Mock<IMetadataStore>();
        metadataStore.Setup(x => x.PutAsync("replay-jobs", It.IsAny<ReplayJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ReplayStartedEvent? published = null;
        var messageBus = new Mock<IMessageBus>();
        messageBus.Setup(x => x.PublishAsync("system-events", It.IsAny<ReplayStartedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, ReplayStartedEvent, CancellationToken>((_, evt, _) => published = evt)
            .Returns(Task.CompletedTask);

        var service = CreateService(metadataStore, messageBus);

        var from = DateTimeOffset.UtcNow.AddHours(-3);
        var to = DateTimeOffset.UtcNow;
        var options = new ReplayOptions { MaxParallelFiles = 2 };
        var job = await service.StartAsync(TenantId, from, to, options, CancellationToken.None);

        published.Should().NotBeNull();
        published!.JobId.Should().Be(job.JobId);
        published.TenantId.Should().Be(TenantId);
        published.Options.Should().Be(options);
    }

    [Fact]
    public async Task GetStatusAsync_WhenJobExists_ReturnsJob()
    {
        var expected = new ReplayJob
        {
            JobId = "job-1",
            TenantId = TenantId,
            From = DateTimeOffset.UtcNow.AddHours(-1),
            To = DateTimeOffset.UtcNow,
            Options = new ReplayOptions(),
            Status = ReplayStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var metadataStore = new Mock<IMetadataStore>();
        metadataStore.Setup(x => x.GetAsync<ReplayJob>("replay-jobs", expected.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var service = CreateService(metadataStore);

        var result = await service.GetStatusAsync(expected.JobId, CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task CancelAsync_UpdatesStatusToFailed()
    {
        var existing = new ReplayJob
        {
            JobId = "job-2",
            TenantId = TenantId,
            From = DateTimeOffset.UtcNow.AddHours(-1),
            To = DateTimeOffset.UtcNow,
            Options = new ReplayOptions(),
            Status = ReplayStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        ReplayJob? updated = null;
        var metadataStore = new Mock<IMetadataStore>();
        metadataStore.Setup(x => x.GetAsync<ReplayJob>("replay-jobs", existing.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        metadataStore.Setup(x => x.PutAsync("replay-jobs", It.IsAny<ReplayJob>(), It.IsAny<CancellationToken>()))
            .Callback<string, ReplayJob, CancellationToken>((_, job, _) => updated = job)
            .Returns(Task.CompletedTask);

        var service = CreateService(metadataStore);

        await service.CancelAsync(existing.JobId, CancellationToken.None);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be(ReplayStatus.Failed);
    }

    private static ReplayService CreateService(Mock<IMetadataStore> metadataStore, Mock<IMessageBus>? messageBus = null)
    {
        var options = Options.Create(new QueryEngineOptions { SystemEventsQueue = "system-events" });
        return new ReplayService(
            new Mock<IObjectStore>().Object,
            messageBus?.Object ?? new Mock<IMessageBus>().Object,
            metadataStore.Object,
            options,
            NullLogger<ReplayService>.Instance);
    }
}
