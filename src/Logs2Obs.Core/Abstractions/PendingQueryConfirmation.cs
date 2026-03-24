namespace Logs2Obs.Core.Abstractions;

public class PendingQueryConfirmation
{
    public required string Token { get; set; }
    public required string TenantId { get; set; }
    public required string Sql { get; set; }
    public required QueryCostEstimate Estimate { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
}
