namespace Logs2Obs.Core.Models;

public sealed record AlertRule
{
    public required string RuleId { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required string Sql { get; init; }
    public required string ThresholdOperator { get; init; }
    public required double ThresholdValue { get; init; }
    public int EvaluationIntervalSeconds { get; init; } = 60;
    public string? NotificationChannel { get; init; }
    public IReadOnlyList<AlertDestination> Destinations { get; init; } = Array.Empty<AlertDestination>();
    public bool IsEnabled { get; init; }
}
