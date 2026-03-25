using Logs2Obs.Puller.Connectors;
using Logs2Obs.Puller.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Logs2Obs.Puller.Tests.Connectors;

/// <summary>
/// Tests for Azure Blob Pull Connector implementation.
/// Awaiting Dolores Phase 6 implementation.
/// </summary>
public class AzureBlobPullConnectorTests
{
    [Fact]
    public async Task PullAsync_WhenContainerHasBlobs_YieldsLogEntries()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.AzureBlob) with
        {
            ConnectorConfig = new Dictionary<string, string>
            {
                ["container"] = "log-container",
                ["prefix"] = "logs"
            }
        };

        var blobKeys = new[]
        {
            "log-container/logs/2026-03-25-1.ndjson",
            "log-container/logs/2026-03-25-2.ndjson"
        };

        var entriesByKey = blobKeys.ToDictionary(
            key => key,
            _ => TestDataBuilders.AValidLogEntry("source-tenant"));

        var objectStore = new Mock<IObjectStore>();
        objectStore
            .Setup(x => x.ListAsync("log-container/logs", It.IsAny<CancellationToken>()))
            .Returns(TestDataBuilders.ToAsyncEnumerable(blobKeys));
        objectStore
            .Setup(x => x.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                TestDataBuilders.AValidNdjsonStream(new[] { entriesByKey[key] }));

        var metadataStore = new InMemoryMetadataStore();
        var connector = new AzureBlobPullConnector(objectStore.Object, metadataStore, Mock.Of<ILogger<AzureBlobPullConnector>>());

        // Act
        var results = new List<LogEntry>();
        await foreach (var entry in connector.PullAsync(config, DateTimeOffset.UtcNow))
        {
            results.Add(entry);
        }

        // Assert
        results.Should().HaveCount(2);
        results.Should().OnlyContain(entry => entry.TenantId == config.TenantId);
    }

    [Fact]
    public async Task PullAsync_WhenContainerIsEmpty_YieldsNothing()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.AzureBlob) with
        {
            ConnectorConfig = new Dictionary<string, string>
            {
                ["container"] = "empty-container"
            }
        };

        var objectStore = new Mock<IObjectStore>();
        objectStore
            .Setup(x => x.ListAsync("empty-container", It.IsAny<CancellationToken>()))
            .Returns(TestDataBuilders.ToAsyncEnumerable(Array.Empty<string>()));

        var metadataStore = new InMemoryMetadataStore();
        var connector = new AzureBlobPullConnector(objectStore.Object, metadataStore, Mock.Of<ILogger<AzureBlobPullConnector>>());

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
    public async Task GetStateAsync_WhenStateExists_ReturnsStateDictionary()
    {
        // Arrange
        var jobId = "blob-job-123";
        var state = new Dictionary<string, string> { ["lastProcessedKey"] = "log-container/logs/file.log" };
        var metadataStore = new InMemoryMetadataStore();
        var connector = new AzureBlobPullConnector(Mock.Of<IObjectStore>(), metadataStore, Mock.Of<ILogger<AzureBlobPullConnector>>());
        await connector.SaveStateAsync(jobId, state);

        // Act
        var result = await connector.GetStateAsync(jobId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(state);
    }

    [Fact]
    public async Task SaveStateAsync_PersistsState()
    {
        // Arrange
        var jobId = "blob-job-456";
        var state = new Dictionary<string, string> { ["lastBlobName"] = "logs/2026/file.log" };
        var metadataStore = new InMemoryMetadataStore();
        var connector = new AzureBlobPullConnector(Mock.Of<IObjectStore>(), metadataStore, Mock.Of<ILogger<AzureBlobPullConnector>>());

        // Act
        await connector.SaveStateAsync(jobId, state);

        // Assert
        metadataStore.TryGet("pullstate", $"pullstate:{jobId}", out var record).Should().BeTrue();
        var storedState = InMemoryMetadataStore.GetPropertyValue<IReadOnlyDictionary<string, string>>(record!, "State");
        storedState.Should().BeEquivalentTo(state);
    }
}
