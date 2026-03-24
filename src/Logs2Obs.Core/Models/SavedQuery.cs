namespace Logs2Obs.Core.Models;

public sealed record SavedQuery
{
    public required string QueryId { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required string Sql { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
