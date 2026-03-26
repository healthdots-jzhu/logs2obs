namespace Logs2Obs.Core.Models;

public sealed record AlertFiredEvent
{
    public required string EventId { get; init; }
    public required string RuleId { get; init; }
    public required string TenantId { get; init; }
    public required string RuleName { get; init; }
    public required double ActualValue { get; init; }
    public required double ThresholdValue { get; init; }
    public required string ThresholdOperator { get; init; }
    public required string? NotificationChannel { get; init; }
    public DateTimeOffset FiredAt { get; init; } = DateTimeOffset.UtcNow;
}
