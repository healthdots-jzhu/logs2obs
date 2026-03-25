using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Models;
using Logs2Obs.Core.Abstractions;

namespace Logs2Obs.Api.Endpoints;

public static class AlertsEndpoints
{
    public static IEndpointRouteBuilder MapAlertsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/alerts")
            .RequireAuthorization()
            .RequireRateLimiting("tenant-query");

        group.MapGet("", ListAlerts)
            .WithName("ListAlerts")
            .WithOpenApi();

        group.MapPost("", CreateAlert)
            .WithName("CreateAlert")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> ListAlerts(
        HttpContext context,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var alerts = new List<object>();
        
        await foreach (var alert in metadataStore.QueryAsync<Dictionary<string, string>>("alerts", _ => true, cancellationToken))
        {
            alerts.Add(alert);
        }

        return Results.Ok(alerts);
    }

    private static async Task<IResult> CreateAlert(
        HttpContext context,
        CreateAlertRequest request,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var alertId = Guid.NewGuid().ToString();

        var alert = new Dictionary<string, string>
        {
            ["alertId"] = alertId,
            ["tenantId"] = tenantId,
            ["name"] = request.Name,
            ["query"] = request.Query,
            ["condition"] = request.Condition,
            ["severity"] = request.Severity,
            ["notificationChannels"] = System.Text.Json.JsonSerializer.Serialize(request.NotificationChannels),
            ["createdAt"] = DateTime.UtcNow.ToString("O"),
            ["isActive"] = "true"
        };

        await metadataStore.PutAsync("alerts", alert, cancellationToken);

        return Results.Ok(new { alertId, name = request.Name });
    }
}
