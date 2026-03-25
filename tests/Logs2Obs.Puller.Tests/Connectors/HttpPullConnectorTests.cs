using System.Net;
using System.Text;
using Logs2Obs.Puller.Connectors;
using Logs2Obs.Puller.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Logs2Obs.Puller.Tests.Connectors;

/// <summary>
/// Tests for HTTP Pull Connector implementation.
/// Awaiting Dolores Phase 6 implementation.
/// </summary>
public class HttpPullConnectorTests
{
    [Fact]
    public async Task PullAsync_WhenUrlReturnsNdjson_YieldsLogEntries()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.Http);
        var entries = new[]
        {
            TestDataBuilders.AValidLogEntry("source-tenant"),
            TestDataBuilders.AValidLogEntry("source-tenant")
        };
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(TestDataBuilders.AValidNdjsonStream(entries))
        };

        var handler = new MockHttpMessageHandler(new[] { response });
        var httpClient = new HttpClient(handler);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var connector = new HttpPullConnector(factory.Object, new InMemoryMetadataStore(), Mock.Of<ILogger<HttpPullConnector>>());

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
    public async Task PullAsync_WhenApiKeyConfigured_SendsApiKeyHeader()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.Http) with
        {
            ConnectorConfig = new Dictionary<string, string>
            {
                ["url"] = "https://example.com/logs",
                ["apiKey"] = "super-secret"
            }
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(TestDataBuilders.AValidNdjsonStream(new[] { TestDataBuilders.AValidLogEntry() }))
        };

        var handler = new MockHttpMessageHandler(new[] { response });
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var connector = new HttpPullConnector(factory.Object, new InMemoryMetadataStore(), Mock.Of<ILogger<HttpPullConnector>>());

        // Act
        await foreach (var _ in connector.PullAsync(config, DateTimeOffset.UtcNow))
        {
        }

        // Assert
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.TryGetValues("X-Api-Key", out var values).Should().BeTrue();
        values.Should().ContainSingle().Which.Should().Be("super-secret");
    }

    [Fact]
    public async Task PullAsync_WhenUrlReturns429_RetriesWithBackoff()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.Http);

        var responses = Enumerable.Range(0, 4)
            .Select(_ => new HttpResponseMessage((HttpStatusCode)429))
            .ToArray();
        var handler = new MockHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var connector = new HttpPullConnector(factory.Object, new InMemoryMetadataStore(), Mock.Of<ILogger<HttpPullConnector>>());

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
    public async Task PullAsync_WhenUrlReturns404_ThrowsHttpRequestException()
    {
        // Arrange
        var config = TestDataBuilders.AValidPullJobConfig(connectorType: ConnectorType.Http);

        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var handler = new MockHttpMessageHandler(new[] { response });
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var connector = new HttpPullConnector(factory.Object, new InMemoryMetadataStore(), Mock.Of<ILogger<HttpPullConnector>>());

        // Act
        var act = async () =>
        {
            await foreach (var _ in connector.PullAsync(config, DateTimeOffset.UtcNow))
            {
            }
        };

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SaveStateAsync_StoresLastPullTimestamp()
    {
        // Arrange
        var jobId = "http-job-123";
        var state = new Dictionary<string, string> { ["lastPullAt"] = "1711372800000" };
        var metadataStore = new InMemoryMetadataStore();
        var connector = new HttpPullConnector(Mock.Of<IHttpClientFactory>(), metadataStore, Mock.Of<ILogger<HttpPullConnector>>());

        // Act
        await connector.SaveStateAsync(jobId, state);

        // Assert
        metadataStore.TryGet("pullstate", $"pullstate:{jobId}", out var record).Should().BeTrue();
        var storedState = InMemoryMetadataStore.GetPropertyValue<IReadOnlyDictionary<string, string>>(record!, "State");
        storedState.Should().BeEquivalentTo(state);
    }
}
