namespace Logs2Obs.QueryEngine.Alerts;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Microsoft.Extensions.Logging;

public sealed class AlertNotificationService(
    IMetadataStore metadataStore,
    IMessageBus messageBus,
    ILogger<AlertNotificationService> logger)
{
    private const string TableName = "alert-fired";

    private readonly IMetadataStore _metadataStore = metadataStore;
    private readonly IMessageBus _messageBus = messageBus;
    private readonly ILogger<AlertNotificationService> _logger = logger;

    public async Task HandleAlertFiredAsync(AlertFiredEvent evt, CancellationToken ct)
    {
        await LogAlertAuditAsync(evt, ct);

        var channel = evt.NotificationChannel?.Trim().ToLowerInvariant();
        switch (channel)
        {
            case "slack":
                _logger.LogWarning(
                    "Alert {RuleName} fired for tenant {TenantId}; Slack dispatch requires webhook configuration",
                    evt.RuleName, evt.TenantId);
                break;
            case "webhook":
                _logger.LogWarning(
                    "Alert {RuleName} fired for tenant {TenantId}; Webhook dispatch requires HttpClient integration",
                    evt.RuleName, evt.TenantId);
                break;
            default:
                _logger.LogInformation(
                    "Alert {RuleName} fired for tenant {TenantId} with value {ActualValue}",
                    evt.RuleName, evt.TenantId, evt.ActualValue);
                break;
        }

        _logger.LogDebug("Alert notification dispatched via {BusType}", _messageBus.GetType().Name);
    }

    public Task LogAlertAuditAsync(AlertFiredEvent evt, CancellationToken ct)
    {
        var record = new AlertFiredRecord
        {
            Key = BuildKey(evt),
            TenantId = evt.TenantId,
            RuleId = evt.RuleId,
            FiredAt = evt.FiredAt,
            Event = evt
        };

        return _metadataStore.PutAsync(TableName, record, ct);
    }

    private static string BuildKey(AlertFiredEvent evt) =>
        $"alert-fired:{evt.TenantId}:{evt.EventId}";

    private sealed record AlertFiredRecord
    {
        public required string Key { get; init; }
        public required string TenantId { get; init; }
        public required string RuleId { get; init; }
        public required DateTimeOffset FiredAt { get; init; }
        public required AlertFiredEvent Event { get; init; }
    }
}
