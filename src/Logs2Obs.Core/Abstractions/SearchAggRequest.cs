namespace Logs2Obs.Core.Abstractions;

public sealed record SearchAggRequest
{
    public required string TenantId { get; init; }
    public required string Field { get; init; }
    public string? Filter { get; init; }
    public int Size { get; init; } = 100;
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}
