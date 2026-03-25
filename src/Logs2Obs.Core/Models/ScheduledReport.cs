namespace Logs2Obs.Core.Models;

public sealed record ScheduledReport
{
    public required string ReportId { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required string SavedQueryId { get; init; }
    public required string CronSchedule { get; init; }
    public required string[] Recipients { get; init; }
    public bool IsEnabled { get; init; } = true;
    public DateTimeOffset? LastRunAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
