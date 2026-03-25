using System.Net;
using System.Text;
using System.Text.Json;
using Logs2Obs.Puller.Connectors;
using Logs2Obs.Puller.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Logs2Obs.Puller.Tests.Connectors;

/// <summary>
/// Tests for CloudWatch Pull Connector implementation.
/// Awaiting Dolores Phase 6 implementation.
/// </summary>
public class CloudWatchPullConnectorTests
{
    [Fact]
    public async Task PullAsync_WhenEndpointReturnsLogs_YieldsLogEntries()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.CloudWatch) with
        {
            ConnectorConfig = new Dictionary<string, string>
            {
                ["endpoint"] = "https://cloudwatch.local",
                ["logGroupName"] = "logs-group"
            }
        };

        var entries = new[]
        {
            TestDataBuilders.AValidLogEntry("source-tenant") with { TimestampUnixMs = 1_711_372_800_000 },
            TestDataBuilders.AValidLogEntry("source-tenant") with { TimestampUnixMs = 1_711_372_800_500 }
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(entries), Encoding.UTF8, "application/json")
        };

        var handler = new MockHttpMessageHandler(new[] { response });
        var httpClient = new HttpClient(handler);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var metadataStore = new InMemoryMetadataStore();
        var connector = new CloudWatchPullConnector(factory.Object, metadataStore, Mock.Of<ILogger<CloudWatchPullConnector>>());

        // Act
        var results = new List<LogEntry>();
        await foreach (var entry in connector.PullAsync(config, DateTimeOffset.UtcNow))
        {
            results.Add(entry);
        }

        // Assert
        results.Should().HaveCount(2);
        results.Should().OnlyContain(entry => entry.TenantId == config.TenantId);
        metadataStore.TryGet("pullstate", $"pullstate:{config.JobId}", out var record).Should().BeTrue();
        var storedState = InMemoryMetadataStore.GetPropertyValue<IReadOnlyDictionary<string, string>>(record!, "State");
        storedState["lastEventTimestamp"].Should()
            .Be(DateTimeOffset.FromUnixTimeMilliseconds(1_711_372_800_500).ToString("O"));
    }

    [Fact]
    public async Task PullAsync_WhenEndpointReturns500_RetriesAndThrows()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.CloudWatch) with
        {
            ConnectorConfig = new Dictionary<string, string>
            {
                ["endpoint"] = "https://cloudwatch.local",
                ["logGroupName"] = "logs-group"
            }
        };

        var responses = Enumerable.Range(0, 4)
            .Select(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))
            .ToArray();
        var handler = new MockHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var connector = new CloudWatchPullConnector(factory.Object, new InMemoryMetadataStore(), Mock.Of<ILogger<CloudWatchPullConnector>>());

        // Act
        var act = async () =>
        {
            await foreach (var _ in connector.PullAsync(config, DateTimeOffset.UtcNow))
            {
            }
        };

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PullAsync_FiltersEntriesSinceTimestamp()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.CloudWatch) with
        {
            ConnectorConfig = new Dictionary<string, string>
            {
                ["endpoint"] = "https://cloudwatch.local",
                ["logGroupName"] = "logs-group"
            }
        };
        var since = new DateTimeOffset(2026, 3, 25, 10, 0, 0, TimeSpan.Zero);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(Array.Empty<LogEntry>()), Encoding.UTF8, "application/json")
        };

        var handler = new MockHttpMessageHandler(new[] { response });
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var connector = new CloudWatchPullConnector(factory.Object, new InMemoryMetadataStore(), Mock.Of<ILogger<CloudWatchPullConnector>>());

        // Act
        await foreach (var _ in connector.PullAsync(config, since))
        {
        }

        // Assert
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.ToString().Should().Contain(Uri.EscapeDataString(since.ToString("O")));
    }

    [Fact]
    public async Task SaveStateAsync_StoresLastEventTimestamp()
    {
        // Arrange
        var jobId = "cloudwatch-job-123";
        var state = new Dictionary<string, string> { ["lastEventTimestamp"] = "1711372800000" };
        var metadataStore = new InMemoryMetadataStore();
        var connector = new CloudWatchPullConnector(
            Mock.Of<IHttpClientFactory>(),
            metadataStore,
            Mock.Of<ILogger<CloudWatchPullConnector>>());

        // Act
        await connector.SaveStateAsync(jobId, state);

        // Assert
        metadataStore.TryGet("pullstate", $"pullstate:{jobId}", out var record).Should().BeTrue();
        var storedState = InMemoryMetadataStore.GetPropertyValue<IReadOnlyDictionary<string, string>>(record!, "State");
        storedState.Should().BeEquivalentTo(state);
    }
}
