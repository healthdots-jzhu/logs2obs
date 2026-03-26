namespace Logs2Obs.QueryEngine.Tests.Alerts;

using Logs2Obs.QueryEngine.Alerts;
using Logs2Obs.QueryEngine.Tests.Helpers;

public class AlertNotificationServiceTests
{
    [Fact]
    public async Task HandleAlertFiredAsync_WhenSlackChannel_LogsDispatch()
    {
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<AlertNotificationService>>();
        var service = new AlertNotificationService(
            new InMemoryMetadataStore(),
            new Mock<IMessageBus>().Object,
            logger.Object);

        var evt = BuildEvent("slack");

        await service.HandleAlertFiredAsync(evt, CancellationToken.None);

        VerifyLog(logger, Microsoft.Extensions.Logging.LogLevel.Warning, "Slack dispatch");
    }

    [Fact]
    public async Task HandleAlertFiredAsync_WhenWebhookChannel_LogsDispatch()
    {
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<AlertNotificationService>>();
        var service = new AlertNotificationService(
            new InMemoryMetadataStore(),
            new Mock<IMessageBus>().Object,
            logger.Object);

        var evt = BuildEvent("webhook");

        await service.HandleAlertFiredAsync(evt, CancellationToken.None);

        VerifyLog(logger, Microsoft.Extensions.Logging.LogLevel.Warning, "Webhook dispatch");
    }

    [Fact]
    public async Task HandleAlertFiredAsync_WhenNoChannel_LogsAuditOnly()
    {
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<AlertNotificationService>>();
        var service = new AlertNotificationService(
            new InMemoryMetadataStore(),
            new Mock<IMessageBus>().Object,
            logger.Object);

        var evt = BuildEvent(null);

        await service.HandleAlertFiredAsync(evt, CancellationToken.None);

        VerifyLog(logger, Microsoft.Extensions.Logging.LogLevel.Information, "Alert");
    }

    [Fact]
    public async Task LogAlertAuditAsync_PersistsToMetadataStore()
    {
        var metadataStore = new InMemoryMetadataStore();
        var service = new AlertNotificationService(
            metadataStore,
            new Mock<IMessageBus>().Object,
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
        Mock<Microsoft.Extensions.Logging.ILogger<AlertNotificationService>> logger,
        Microsoft.Extensions.Logging.LogLevel level,
        string expectedMessageFragment)
    {
        var matches = logger.Invocations
            .Where(invocation => invocation.Method.Name == "Log")
            .Where(invocation => invocation.Arguments[0] is Microsoft.Extensions.Logging.LogLevel logLevel && logLevel == level)
            .ToList();

        matches.Should().ContainSingle();
        matches[0].Arguments[2].ToString().Should().Contain(expectedMessageFragment);
    }
}
