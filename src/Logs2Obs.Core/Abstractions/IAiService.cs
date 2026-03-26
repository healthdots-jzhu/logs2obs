namespace Logs2Obs.Core.Abstractions;

using Logs2Obs.Core.Graphs;
using Logs2Obs.Core.Models;

/// <summary>AI service for natural language to SQL translation and query assistance.</summary>
public interface IAiService
{
    /// <summary>Translates a natural language query to SQL using the tenant's schema context.</summary>
    Task<AiSqlResult> GenerateSqlAsync(string tenantId, string naturalLanguage, string schemaContext, CancellationToken ct = default);

    Task<NlQueryResult> TranslateToSqlAsync(
        string naturalLanguage,
        QueryContext ctx,
        CancellationToken ct = default);

    Task<IReadOnlyList<GraphSuggestion>> SuggestGraphsAsync(
        QueryResultSchema schema,
        string? intent,
        CancellationToken ct = default);
}
