using Logs2Obs.Puller.Services;
using Logs2Obs.Puller.Tests.Helpers;

namespace Logs2Obs.Puller.Tests.Services;

/// <summary>
/// Tests for Pull Job State Service (manages PullJobConfig persistence).
/// Awaiting Dolores Phase 6 implementation.
/// </summary>
public class PullJobStateServiceTests
{
    [Fact]
    public async Task GetJobAsync_WhenJobExists_ReturnsConfig()
    {
        // Arrange
        var jobId = "existing-job";
        var expectedConfig = TestDataBuilders.AValidPullJobConfig(jobId: jobId);
        var metadataStore = new InMemoryMetadataStore();
        var service = new PullJobStateService(metadataStore);
        await service.SaveJobAsync(expectedConfig, CancellationToken.None);

        // Act
        var result = await service.GetJobAsync(jobId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedConfig);
    }

    [Fact]
    public async Task GetJobAsync_WhenJobNotFound_ReturnsNull()
    {
        // Arrange
        var jobId = "nonexistent-job";
        var metadataStore = new InMemoryMetadataStore();
        var service = new PullJobStateService(metadataStore);

        // Act
        var result = await service.GetJobAsync(jobId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveJobAsync_PersistsToMetadataStore()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig();
        var metadataStore = new InMemoryMetadataStore();
        var service = new PullJobStateService(metadataStore);

        // Act
        await service.SaveJobAsync(config, CancellationToken.None);

        // Assert
        metadataStore.TryGet("pulljob", $"pulljob:{config.TenantId}:{config.JobId}", out var record).Should().BeTrue();
        var storedConfig = InMemoryMetadataStore.GetPropertyValue<PullJobConfig>(record!, "Config");
        storedConfig.Should().BeEquivalentTo(config);
    }

    [Fact]
    public async Task ListJobsAsync_ReturnsTenantJobs()
    {
        // Arrange
        var tenantId = "tenant-abc";
        var tenantJobs = new[]
        {
            TestDataBuilders.AValidPullJobConfig(jobId: "job-1", tenantId: tenantId),
            TestDataBuilders.AValidPullJobConfig(jobId: "job-2", tenantId: tenantId)
        };
        var otherJob = TestDataBuilders.AValidPullJobConfig(jobId: "job-3", tenantId: "tenant-other");
        var metadataStore = new InMemoryMetadataStore();
        var service = new PullJobStateService(metadataStore);
        foreach (var job in tenantJobs)
        {
            await service.SaveJobAsync(job, CancellationToken.None);
        }
        await service.SaveJobAsync(otherJob, CancellationToken.None);

        // Act
        var results = new List<PullJobConfig>();
        await foreach (var job in service.ListJobsAsync(tenantId, CancellationToken.None))
        {
            results.Add(job);
        }

        // Assert
        results.Should().BeEquivalentTo(tenantJobs);
    }

    [Fact]
    public async Task DeleteJobAsync_RemovesFromMetadataStore()
    {
        // Arrange
        var jobId = "job-to-delete";
        var config = TestDataBuilders.AValidPullJobConfig(jobId: jobId);
        var metadataStore = new InMemoryMetadataStore();
        var service = new PullJobStateService(metadataStore);
        await service.SaveJobAsync(config, CancellationToken.None);

        // Act
        await service.DeleteJobAsync(jobId, CancellationToken.None);

        // Assert
        metadataStore.TryGet("pulljob", $"pulljob:{config.TenantId}:{config.JobId}", out _).Should().BeFalse();
    }
}
