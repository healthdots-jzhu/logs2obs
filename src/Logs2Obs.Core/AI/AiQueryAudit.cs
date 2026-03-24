namespace Logs2Obs.Core.AI;

using Logs2Obs.Core.Abstractions;

/// <summary>Immutable audit record for every AI-generated SQL query, stored in object storage.</summary>
public sealed record AiQueryAudit
{
    public required string QueryId { get; init; }
    public required string TenantId { get; init; }
    public required string NaturalLanguageInput { get; init; }
    public required string SystemPrompt { get; init; }
    public required string GeneratedSql { get; init; }
    public required string Explanation { get; init; }
    public required string SuggestedGraphType { get; init; }
    public required SqlSafetyReport SafetyReport { get; init; }
    public required int InputTokenCount { get; init; }
    public required int OutputTokenCount { get; init; }
    public required string ModelUsed { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}
