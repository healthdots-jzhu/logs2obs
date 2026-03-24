namespace Logs2Obs.Core.Abstractions;

/// <summary>AI service for natural language to SQL translation and query assistance.</summary>
public interface IAiService
{
    /// <summary>Translates a natural language query to SQL using the tenant's schema context.</summary>
    Task<AiSqlResult> GenerateSqlAsync(string tenantId, string naturalLanguage, string schemaContext, CancellationToken ct = default);
}
