using Logs2Obs.Puller.Tests.Helpers;

namespace Logs2Obs.Puller.Tests.Pipeline;

/// <summary>
/// Tests for IPullConnector interface contract.
/// These tests verify the Core abstraction behavior using mocks.
/// </summary>
public class PullJobPipelineTests
{
    [Fact]
    public async Task IPullConnector_PullAsync_YieldsEntries()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig();
        var expectedEntries = new[]
        {
            TestDataBuilders.AValidLogEntry(),
            TestDataBuilders.AValidLogEntry(),
            TestDataBuilders.AValidLogEntry()
        };

        var mockConnector = new Mock<IPullConnector>();
        mockConnector
            .Setup(x => x.PullAsync(config, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(expectedEntries));

        // Act
        var actualEntries = new List<LogEntry>();
        await foreach (var entry in mockConnector.Object.PullAsync(config, DateTimeOffset.UtcNow, CancellationToken.None))
        {
            actualEntries.Add(entry);
        }

        // Assert
        actualEntries.Should().HaveCount(3);
        actualEntries.Should().BeEquivalentTo(expectedEntries);
    }

    [Fact]
    public async Task IPullConnector_GetStateAsync_WhenNoState_ReturnsNull()
    {
        // Arrange
        var mockConnector = new Mock<IPullConnector>();
        mockConnector
            .Setup(x => x.GetStateAsync("job-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string>?)null);

        // Act
        var state = await mockConnector.Object.GetStateAsync("job-123", CancellationToken.None);

        // Assert
        state.Should().BeNull();
    }

    [Fact]
    public async Task IPullConnector_SaveAndGetState_RoundTrip()
    {
        // Arrange
        var jobId = "job-456";
        var stateToSave = new Dictionary<string, string>
        {
            ["lastProcessedKey"] = "s3://bucket/2026/03/25/file123.log",
            ["lastTimestamp"] = "1711372800000"
        };

        IReadOnlyDictionary<string, string>? capturedState = null;

        var mockConnector = new Mock<IPullConnector>();
        mockConnector
            .Setup(x => x.SaveStateAsync(jobId, It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyDictionary<string, string>, CancellationToken>((_, state, _) => capturedState = state)
            .Returns(Task.CompletedTask);

        mockConnector
            .Setup(x => x.GetStateAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => capturedState);

        // Act
        await mockConnector.Object.SaveStateAsync(jobId, stateToSave, CancellationToken.None);
        var retrievedState = await mockConnector.Object.GetStateAsync(jobId, CancellationToken.None);

        // Assert
        retrievedState.Should().NotBeNull();
        retrievedState.Should().BeEquivalentTo(stateToSave);
        retrievedState!["lastProcessedKey"].Should().Be("s3://bucket/2026/03/25/file123.log");
        retrievedState["lastTimestamp"].Should().Be("1711372800000");
    }

    [Fact]
    public void PullJobConfig_WithAllRequiredFields_IsConstructedCorrectly()
    {
        // Arrange & Act
        var config = TestDataBuilders.AValidPullJobConfig(
            jobId: "test-job",
            tenantId: "tenant-123",
            connectorType: ConnectorType.AwsS3,
            schedule: "0 */15 * * * ?");

        // Assert
        config.Should().NotBeNull();
        config.JobId.Should().Be("test-job");
        config.TenantId.Should().Be("tenant-123");
        config.ConnectorType.Should().Be(ConnectorType.AwsS3);
        config.Schedule.Should().Be("0 */15 * * * ?");
        config.ConnectorConfig.Should().NotBeNull();
        config.IsEnabled.Should().BeTrue();
    }

    private static async IAsyncEnumerable<LogEntry> ToAsyncEnumerable(IEnumerable<LogEntry> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
