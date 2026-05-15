using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Models;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Validation;

namespace Logs2Obs.Api.Endpoints;

public static class AlertsEndpoints
{
    private const string AlertRulesTable = "alert-rules";
    private static readonly HashSet<string> SupportedThresholdOperators = new(StringComparer.Ordinal)
    {
        ">",
        "<",
        ">=",
        "<=",
        "==",
        "="
    };

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
        var alerts = new List<AlertRule>();
        
        await foreach (var alert in metadataStore.QueryAsync<AlertRule>(
            AlertRulesTable,
            rule => rule.TenantId == tenantId,
            cancellationToken))
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
        var validationErrors = ValidateCreateAlertRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.BadRequest(new { errors = validationErrors });
        }

        var alertId = Guid.CreateVersion7().ToString("N");
        var sql = request.Sql ?? request.Query;

        var thresholdOperator = NormalizeOperator(request.ComparisonOperator ?? ExtractOperator(request.Condition));
        var threshold = request.Threshold ?? ExtractThreshold(request.Condition);
        var destinations = BuildDestinations(request);
        var notificationChannel = destinations.Count == 1
            ? destinations[0].Type
            : BuildLegacyNotificationChannel(request.NotificationChannels);
        var alert = new AlertRule
        {
            RuleId = alertId,
            TenantId = tenantId,
            Name = request.Name,
            Sql = sql!,
            ThresholdOperator = thresholdOperator!,
            ThresholdValue = threshold!.Value,
            EvaluationIntervalSeconds = Math.Max(1, request.EvaluationIntervalMinutes ?? 1) * 60,
            NotificationChannel = notificationChannel,
            Destinations = destinations,
            IsEnabled = true
        };

        await metadataStore.PutAsync(AlertRulesTable, alert, cancellationToken);

        return Results.Created($"/api/v1/alerts/{alertId}", new { alertId, name = request.Name });
    }

    private static List<string> ValidateCreateAlertRequest(CreateAlertRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add("Alert name is required.");
        }
        else if (request.Name.Length > 256)
        {
            errors.Add("Alert name must be 256 characters or fewer.");
        }

        var sql = request.Sql ?? request.Query;
        if (string.IsNullOrWhiteSpace(sql))
        {
            errors.Add("Alert SQL is required.");
        }
        else if (sql.Length > 20_000)
        {
            errors.Add("Alert SQL must be 20000 characters or fewer.");
        }

        var thresholdOperator = NormalizeOperator(request.ComparisonOperator ?? ExtractOperator(request.Condition));
        if (thresholdOperator is null)
        {
            errors.Add("Alert comparison operator is required.");
        }
        else if (!SupportedThresholdOperators.Contains(thresholdOperator))
        {
            errors.Add("Alert comparison operator must be one of: >, <, >=, <=, ==, =.");
        }

        var threshold = request.Threshold ?? ExtractThreshold(request.Condition);
        if (!threshold.HasValue)
        {
            errors.Add("Alert threshold is required.");
        }
        else if (double.IsNaN(threshold.Value) || double.IsInfinity(threshold.Value))
        {
            errors.Add("Alert threshold must be a finite number.");
        }

        if (request.EvaluationIntervalMinutes is < 1 or > 1440)
        {
            errors.Add("EvaluationIntervalMinutes must be between 1 and 1440.");
        }

        if (request.NotificationChannels is { Count: > 10 })
        {
            errors.Add("A maximum of 10 legacy notification channels is supported.");
        }

        if (request.NotificationChannels is { Count: > 0 }
            && request.NotificationChannels.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Legacy notification channels cannot be blank.");
        }

        var destinations = BuildDestinations(request);
        if (destinations.Count > 10)
        {
            errors.Add("A maximum of 10 alert destinations is supported.");
        }

        var destinationValidator = new AlertDestinationValidator();
        for (var index = 0; index < destinations.Count; index++)
        {
            var result = destinationValidator.Validate(destinations[index]);
            errors.AddRange(result.Errors.Select(error => $"Destinations[{index}].{error.ErrorMessage}"));
        }

        return errors;
    }

    private static List<AlertDestination> BuildDestinations(CreateAlertRequest request)
    {
        if (request.Destinations is { Count: > 0 })
        {
            return request.Destinations
                .Select(destination => destination with
                {
                    Type = destination.Type?.Trim().ToLowerInvariant() ?? string.Empty,
                    WebhookUrl = destination.WebhookUrl?.Trim(),
                    IntegrationKey = destination.IntegrationKey?.Trim()
                })
                .ToList();
        }

        return [];
    }

    private static string? BuildLegacyNotificationChannel(IReadOnlyList<string>? notificationChannels) =>
        notificationChannels?
            .FirstOrDefault(channel => !string.IsNullOrWhiteSpace(channel))
            ?.Trim()
            .ToLowerInvariant();

    private static string? NormalizeOperator(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim() switch
        {
            "GreaterThan" => ">",
            "LessThan" => "<",
            "Equal" => "==",
            "Equals" => "==",
            var op => op
        };
    }

    private static string? ExtractOperator(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return null;

        if (condition.Contains(">=", StringComparison.Ordinal))
            return ">=";
        if (condition.Contains("<=", StringComparison.Ordinal))
            return "<=";
        if (condition.Contains("==", StringComparison.Ordinal))
            return "==";
        if (condition.Contains('>'))
            return ">";
        if (condition.Contains('<'))
            return "<";
        if (condition.Contains('='))
            return "=";

        return condition;
    }

    private static double? ExtractThreshold(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return null;

        var match = System.Text.RegularExpressions.Regex.Match(
            condition,
            @"[-+]?\d+(\.\d+)?",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        return match.Success && double.TryParse(
            match.Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var threshold)
            ? threshold
            : null;
    }
}
