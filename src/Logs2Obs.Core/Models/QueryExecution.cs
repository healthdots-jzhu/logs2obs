namespace Logs2Obs.Core.Models;

public sealed record QueryExecution
{
    public required string ExecutionId { get; init; }
    public required string TenantId { get; init; }
    public required string Sql { get; init; }
    public required QueryStatus Status { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? ResultLocation { get; init; }
    public string? ErrorMessage { get; init; }
}
