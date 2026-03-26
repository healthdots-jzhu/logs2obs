namespace Logs2Obs.QueryEngine.AI;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Graphs;
using Logs2Obs.Core.Models;

public sealed class NoOpAiService : IAiService
{
    public Task<AiSqlResult> GenerateSqlAsync(
        string tenantId,
        string naturalLanguage,
        string schemaContext,
        CancellationToken ct = default)
    {
        _ = tenantId;
        _ = naturalLanguage;
        _ = schemaContext;
        _ = ct;

        return Task.FromResult(new AiSqlResult
        {
            Sql = "SELECT 1",
            Explanation = "AI disabled",
            SuggestedGraphType = GraphType.LineChart,
            InputTokenCount = 0,
            OutputTokenCount = 0,
            ModelUsed = "none"
        });
    }

    public Task<NlQueryResult> TranslateToSqlAsync(
        string naturalLanguage,
        QueryContext ctx,
        CancellationToken ct = default)
    {
        _ = naturalLanguage;
        _ = ctx;
        _ = ct;

        return Task.FromResult(new NlQueryResult
        {
            Sql = "SELECT 1",
            Explanation = "AI disabled",
            SuggestedGraphType = GraphType.LineChart
        });
    }

    public Task<IReadOnlyList<GraphSuggestion>> SuggestGraphsAsync(
        QueryResultSchema schema,
        string? intent,
        CancellationToken ct = default)
    {
        _ = schema;
        _ = intent;
        _ = ct;

        return Task.FromResult<IReadOnlyList<GraphSuggestion>>([]);
    }
}
