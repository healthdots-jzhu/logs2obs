using Logs2Obs.Puller.Connectors;
using Logs2Obs.Puller.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Logs2Obs.Puller.Tests.Connectors;

/// <summary>
/// Tests for AWS S3 Pull Connector implementation.
/// Awaiting Dolores Phase 6 implementation.
/// </summary>
public class AwsS3PullConnectorTests
{
    [Fact]
    public async Task PullAsync_WhenObjectStoreHasObjects_YieldsLogEntries()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.AwsS3) with
        {
            ConnectorConfig = new Dictionary<string, string>
            {
                ["bucket"] = "test-bucket",
                ["prefix"] = "logs"
            }
        };

        var objectKeys = new[]
        {
            "test-bucket/logs/2026-03-25-1.ndjson",
            "test-bucket/logs/2026-03-25-2.ndjson"
        };

        var entriesByKey = objectKeys.ToDictionary(
            key => key,
            _ => TestDataBuilders.AValidLogEntry("source-tenant"));

        var objectStore = new Mock<IObjectStore>();
        objectStore
            .Setup(x => x.ListAsync("test-bucket/logs", It.IsAny<CancellationToken>()))
            .Returns(TestDataBuilders.ToAsyncEnumerable(objectKeys));
        objectStore
            .Setup(x => x.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                TestDataBuilders.AValidNdjsonStream(new[] { entriesByKey[key] }));

        var metadataStore = new InMemoryMetadataStore();
        var connector = new AwsS3PullConnector(objectStore.Object, metadataStore, Mock.Of<ILogger<AwsS3PullConnector>>());

        // Act
        var results = new List<LogEntry>();
        await foreach (var entry in connector.PullAsync(config, DateTimeOffset.UtcNow))
        {
            results.Add(entry);
        }

        // Assert
        results.Should().HaveCount(2);
        results.Should().OnlyContain(entry => entry.TenantId == config.TenantId);
        objectStore.Verify(x => x.ReadAsync("test-bucket/logs/2026-03-25-1.ndjson", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PullAsync_WhenObjectStoreIsEmpty_YieldsNothing()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.AwsS3) with
        {
            ConnectorConfig = new Dictionary<string, string>
            {
                ["bucket"] = "empty-bucket"
            }
        };

        var objectStore = new Mock<IObjectStore>();
        objectStore
            .Setup(x => x.ListAsync("empty-bucket", It.IsAny<CancellationToken>()))
            .Returns(TestDataBuilders.ToAsyncEnumerable(Array.Empty<string>()));

        var metadataStore = new InMemoryMetadataStore();
        var connector = new AwsS3PullConnector(objectStore.Object, metadataStore, Mock.Of<ILogger<AwsS3PullConnector>>());

        // Act
        var results = new List<LogEntry>();
        await foreach (var entry in connector.PullAsync(config, DateTimeOffset.UtcNow))
        {
            results.Add(entry);
        }

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task PullAsync_WhenObjectStoreThrows_RetriesAndEventuallyThrows()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.AwsS3) with
        {
            ConnectorConfig = new Dictionary<string, string>
            {
                ["bucket"] = "boom-bucket"
            }
        };

        var attempts = 0;
        var objectStore = new Mock<IObjectStore>();
        objectStore
            .Setup(x => x.ListAsync("boom-bucket", It.IsAny<CancellationToken>()))
            .Callback(() => attempts++)
            .Throws(new InvalidOperationException("boom"));

        var metadataStore = new InMemoryMetadataStore();
        var connector = new AwsS3PullConnector(objectStore.Object, metadataStore, Mock.Of<ILogger<AwsS3PullConnector>>());

        // Act
        var act = async () =>
        {
            await foreach (var _ in connector.PullAsync(config, DateTimeOffset.UtcNow))
            {
            }
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(4);
    }

    [Fact]
    public async Task GetStateAsync_WhenStateExists_ReturnsStateDictionary()
    {
        // Arrange
        var jobId = "s3-job-123";
        var state = new Dictionary<string, string> { ["lastProcessedKey"] = "test-bucket/logs/file.log" };
        var metadataStore = new InMemoryMetadataStore();
        var connector = new AwsS3PullConnector(Mock.Of<IObjectStore>(), metadataStore, Mock.Of<ILogger<AwsS3PullConnector>>());
        await connector.SaveStateAsync(jobId, state);

        // Act
        var result = await connector.GetStateAsync(jobId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(state);
    }

    [Fact]
    public async Task GetStateAsync_WhenNoState_ReturnsNull()
    {
        // Arrange
        var jobId = "s3-job-456";
        var metadataStore = new InMemoryMetadataStore();
        var connector = new AwsS3PullConnector(Mock.Of<IObjectStore>(), metadataStore, Mock.Of<ILogger<AwsS3PullConnector>>());

        // Act
        var result = await connector.GetStateAsync(jobId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveStateAsync_PersistsStateToMetadataStore()
    {
        // Arrange
        var jobId = "s3-job-789";
        var state = new Dictionary<string, string> { ["lastProcessedKey"] = "s3://bucket/file.log" };
        var metadataStore = new InMemoryMetadataStore();
        var connector = new AwsS3PullConnector(Mock.Of<IObjectStore>(), metadataStore, Mock.Of<ILogger<AwsS3PullConnector>>());

        // Act
        await connector.SaveStateAsync(jobId, state);

        // Assert
        metadataStore.TryGet("pullstate", $"pullstate:{jobId}", out var record).Should().BeTrue();
        var storedState = InMemoryMetadataStore.GetPropertyValue<IReadOnlyDictionary<string, string>>(record!, "State");
        storedState.Should().BeEquivalentTo(state);
    }

    [Fact]
    public async Task PullAsync_FiltersByLastProcessedKey()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.AwsS3) with
        {
            ConnectorConfig = new Dictionary<string, string>
            {
                ["bucket"] = "filter-bucket",
                ["prefix"] = "logs"
            }
        };

        var objectStore = new Mock<IObjectStore>();
        objectStore
            .Setup(x => x.ListAsync("filter-bucket/logs", It.IsAny<CancellationToken>()))
            .Returns(TestDataBuilders.ToAsyncEnumerable(new[]
            {
                "filter-bucket/logs/file-1.ndjson",
                "filter-bucket/logs/file-2.ndjson"
            }));

        objectStore
            .Setup(x => x.ReadAsync("filter-bucket/logs/file-2.ndjson", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestDataBuilders.AValidNdjsonStream(new[] { TestDataBuilders.AValidLogEntry() }));

        var metadataStore = new InMemoryMetadataStore();
        var connector = new AwsS3PullConnector(objectStore.Object, metadataStore, Mock.Of<ILogger<AwsS3PullConnector>>());
        await connector.SaveStateAsync(config.JobId, new Dictionary<string, string>
        {
            ["lastProcessedKey"] = "filter-bucket/logs/file-1.ndjson"
        });

        // Act
        var results = new List<LogEntry>();
        await foreach (var entry in connector.PullAsync(config, DateTimeOffset.UtcNow))
        {
            results.Add(entry);
        }

        // Assert
        results.Should().HaveCount(1);
        objectStore.Verify(x => x.ReadAsync("filter-bucket/logs/file-1.ndjson", It.IsAny<CancellationToken>()), Times.Never);
        objectStore.Verify(x => x.ReadAsync("filter-bucket/logs/file-2.ndjson", It.IsAny<CancellationToken>()), Times.Once);
    }
}
