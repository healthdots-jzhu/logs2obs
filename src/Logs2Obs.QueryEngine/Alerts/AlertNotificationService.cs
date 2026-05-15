namespace Logs2Obs.QueryEngine.Alerts;

using System.Net.Http.Json;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Microsoft.Extensions.Logging;

public sealed class AlertNotificationService(
    IMetadataStore metadataStore,
    IMessageBus messageBus,
    IHttpClientFactory httpClientFactory,
    ILogger<AlertNotificationService> logger)
{
    public const string HttpClientName = "alert-hooks";
    private const string TableName = "alert-fired";

    private readonly IMetadataStore _metadataStore = metadataStore;
    private readonly IMessageBus _messageBus = messageBus;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<AlertNotificationService> _logger = logger;

    public async Task HandleAlertFiredAsync(AlertFiredEvent evt, CancellationToken ct)
    {
        await LogAlertAuditAsync(evt, ct);

        if (evt.Destinations.Count > 0)
        {
            foreach (var destination in evt.Destinations)
            {
                await DispatchDestinationAsync(evt, destination, ct);
            }

            _logger.LogDebug("Alert notification dispatched via {BusType}", _messageBus.GetType().Name);
            return;
        }

        var channel = evt.NotificationChannel?.Trim().ToLowerInvariant();
        switch (channel)
        {
            case "slack":
                _logger.LogWarning(
                    "Alert {RuleName} fired for tenant {TenantId}; Slack dispatch requires a webhook URL",
                    evt.RuleName, evt.TenantId);
                break;
            case "webhook":
                _logger.LogWarning(
                    "Alert {RuleName} fired for tenant {TenantId}; Webhook dispatch requires a webhook URL",
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

    private async Task DispatchDestinationAsync(AlertFiredEvent evt, AlertDestination destination, CancellationToken ct)
    {
        var type = destination.Type.Trim().ToLowerInvariant();
        try
        {
            switch (type)
            {
                case "slack":
                    await PostSlackAsync(evt, destination, ct);
                    break;
                case "webhook":
                    await PostWebhookAsync(evt, destination, ct);
                    break;
                default:
                    _logger.LogWarning(
                        "Alert {RuleName} fired for tenant {TenantId}; unsupported hook type {HookType}",
                        evt.RuleName, evt.TenantId, destination.Type);
                    break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(
                ex,
                "Alert {RuleName} hook delivery failed for tenant {TenantId} via {HookType}",
                evt.RuleName,
                evt.TenantId,
                destination.Type);
        }
    }

    private async Task PostSlackAsync(AlertFiredEvent evt, AlertDestination destination, CancellationToken ct)
    {
        var uri = BuildWebhookUri(destination);
        if (uri is null)
        {
            _logger.LogWarning(
                "Alert {RuleName} fired for tenant {TenantId}; Slack hook skipped because webhook URL is missing or invalid",
                evt.RuleName, evt.TenantId);
            return;
        }

        var payload = new
        {
            text = $"{evt.RuleName} fired: {evt.ActualValue} {evt.ThresholdOperator} {evt.ThresholdValue}",
            alert = evt
        };

        await PostJsonAsync(uri, payload, ct);
        _logger.LogInformation(
            "Alert {RuleName} Slack hook delivered for tenant {TenantId}",
            evt.RuleName, evt.TenantId);
    }

    private async Task PostWebhookAsync(AlertFiredEvent evt, AlertDestination destination, CancellationToken ct)
    {
        var uri = BuildWebhookUri(destination);
        if (uri is null)
        {
            _logger.LogWarning(
                "Alert {RuleName} fired for tenant {TenantId}; webhook skipped because URL is missing or invalid",
                evt.RuleName, evt.TenantId);
            return;
        }

        await PostJsonAsync(uri, evt, ct);
        _logger.LogInformation(
            "Alert {RuleName} webhook delivered for tenant {TenantId}",
            evt.RuleName, evt.TenantId);
    }

    private async Task PostJsonAsync<T>(Uri uri, T payload, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync(uri, payload, ct);
        response.EnsureSuccessStatusCode();
    }

    private static Uri? BuildWebhookUri(AlertDestination destination)
    {
        if (string.IsNullOrWhiteSpace(destination.WebhookUrl)
            || !Uri.TryCreate(destination.WebhookUrl, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
            return uri;

        return uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            ? uri
            : null;
    }

    private sealed record AlertFiredRecord
    {
        public required string Key { get; init; }
        public required string TenantId { get; init; }
        public required string RuleId { get; init; }
        public required DateTimeOffset FiredAt { get; init; }
        public required AlertFiredEvent Event { get; init; }
    }
}
