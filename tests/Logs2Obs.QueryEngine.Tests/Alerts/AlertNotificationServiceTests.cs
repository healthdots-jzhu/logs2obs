namespace Logs2Obs.QueryEngine.Tests.Alerts;

using System.Net;
using Logs2Obs.QueryEngine.Alerts;
using Logs2Obs.QueryEngine.Tests.Helpers;
using Microsoft.Extensions.Logging;

public class AlertNotificationServiceTests
{
    [Fact]
    public async Task HandleAlertFiredAsync_WhenSlackChannel_LogsDispatch()
    {
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<AlertNotificationService>>();
        var service = new AlertNotificationService(
            new InMemoryMetadataStore(),
            new Mock<IMessageBus>().Object,
            new TestHttpClientFactory(new CaptureHandler()),
            logger.Object);

        var evt = BuildEvent("slack");

        await service.HandleAlertFiredAsync(evt, CancellationToken.None);

        VerifyLog(logger, LogLevel.Warning, "Slack");
    }

    [Fact]
    public async Task HandleAlertFiredAsync_WhenWebhookChannel_LogsDispatch()
    {
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<AlertNotificationService>>();
        var service = new AlertNotificationService(
            new InMemoryMetadataStore(),
            new Mock<IMessageBus>().Object,
            new TestHttpClientFactory(new CaptureHandler()),
            logger.Object);

        var evt = BuildEvent("webhook");

        await service.HandleAlertFiredAsync(evt, CancellationToken.None);

        VerifyLog(logger, LogLevel.Warning, "Webhook");
    }

    [Fact]
    public async Task HandleAlertFiredAsync_WhenNoChannel_LogsAuditOnly()
    {
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<AlertNotificationService>>();
        var service = new AlertNotificationService(
            new InMemoryMetadataStore(),
            new Mock<IMessageBus>().Object,
            new TestHttpClientFactory(new CaptureHandler()),
            logger.Object);

        var evt = BuildEvent(null);

        await service.HandleAlertFiredAsync(evt, CancellationToken.None);

        VerifyLog(logger, LogLevel.Information, "Alert");
    }

    [Fact]
    public async Task HandleAlertFiredAsync_WhenWebhookDestination_PostsEventPayload()
    {
        var handler = new CaptureHandler();
        var service = new AlertNotificationService(
            new InMemoryMetadataStore(),
            new Mock<IMessageBus>().Object,
            new TestHttpClientFactory(handler),
            NullLogger<AlertNotificationService>.Instance);

        var evt = BuildEvent("webhook") with
        {
            Destinations = new[]
            {
                new AlertDestination
                {
                    Type = "webhook",
                    WebhookUrl = "https://hooks.example.test/logs2obs"
                }
            }
        };

        await service.HandleAlertFiredAsync(evt, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri.Should().Be("https://hooks.example.test/logs2obs");
        handler.RequestBodies[0].Should().Contain(evt.EventId);
    }

    [Fact]
    public async Task HandleAlertFiredAsync_WhenSlackDestination_PostsSlackPayload()
    {
        var handler = new CaptureHandler();
        var service = new AlertNotificationService(
            new InMemoryMetadataStore(),
            new Mock<IMessageBus>().Object,
            new TestHttpClientFactory(handler),
            NullLogger<AlertNotificationService>.Instance);

        var evt = BuildEvent("slack") with
        {
            Destinations = new[]
            {
                new AlertDestination
                {
                    Type = "slack",
                    WebhookUrl = "https://hooks.slack.test/services/T000/B000/secret"
                }
            }
        };

        await service.HandleAlertFiredAsync(evt, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.RequestBodies[0].Should().Contain("\"text\"");
        handler.RequestBodies[0].Should().Contain(evt.RuleName);
    }

    [Fact]
    public async Task HandleAlertFiredAsync_WhenWebhookUrlIsInvalid_DoesNotPost()
    {
        var handler = new CaptureHandler();
        var logger = new Mock<ILogger<AlertNotificationService>>();
        var service = new AlertNotificationService(
            new InMemoryMetadataStore(),
            new Mock<IMessageBus>().Object,
            new TestHttpClientFactory(handler),
            logger.Object);

        var evt = BuildEvent("webhook") with
        {
            Destinations = new[]
            {
                new AlertDestination
                {
                    Type = "webhook",
                    WebhookUrl = "ftp://hooks.example.test/not-allowed"
                }
            }
        };

        await service.HandleAlertFiredAsync(evt, CancellationToken.None);

        handler.Requests.Should().BeEmpty();
        VerifyLog(logger, LogLevel.Warning, "invalid");
    }

    [Fact]
    public async Task LogAlertAuditAsync_PersistsToMetadataStore()
    {
        var metadataStore = new InMemoryMetadataStore();
        var service = new AlertNotificationService(
            metadataStore,
            new Mock<IMessageBus>().Object,
            new TestHttpClientFactory(new CaptureHandler()),
            NullLogger<AlertNotificationService>.Instance);

        var evt = BuildEvent("slack");

        await service.LogAlertAuditAsync(evt, CancellationToken.None);

        var key = $"alert-fired:{evt.TenantId}:{evt.EventId}";
        metadataStore.TryGet("alert-fired", key, out var record).Should().BeTrue();
        InMemoryMetadataStore.GetPropertyValue<string>(record!, "RuleId").Should().Be(evt.RuleId);
        InMemoryMetadataStore.GetPropertyValue<DateTimeOffset>(record!, "FiredAt").Should().Be(evt.FiredAt);
        InMemoryMetadataStore.GetPropertyValue<AlertFiredEvent>(record!, "Event").Should().Be(evt);
    }

    private static AlertFiredEvent BuildEvent(string? channel) => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        RuleId = "rule-1",
        TenantId = "tenant-1",
        RuleName = "High Error Rate",
        ActualValue = 10.0,
        ThresholdValue = 5.0,
        ThresholdOperator = ">",
        NotificationChannel = channel
    };

    private static void VerifyLog(
        Mock<ILogger<AlertNotificationService>> logger,
        LogLevel level,
        string expectedMessageFragment)
    {
        var matches = logger.Invocations
            .Where(invocation => invocation.Method.Name == "Log")
            .Where(invocation => invocation.Arguments[0] is LogLevel logLevel && logLevel == level)
            .ToList();

        matches.Should().ContainSingle();
        matches[0].Arguments[2].ToString().Should().Contain(expectedMessageFragment);
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}
