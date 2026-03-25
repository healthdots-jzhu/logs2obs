using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Models;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Graphs;

namespace Logs2Obs.Api.Endpoints;

public static class GraphsEndpoints
{
    public static IEndpointRouteBuilder MapGraphsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/graphs")
            .RequireAuthorization()
            .RequireRateLimiting("tenant-query");

        group.MapPost("/suggest", SuggestGraphs)
            .WithName("SuggestGraphs")
            .WithOpenApi();

        group.MapPost("/render", RenderGraph)
            .WithName("RenderGraph")
            .WithOpenApi();

        group.MapGet("/prebuilt", GetPrebuiltGraphs)
            .WithName("GetPrebuiltGraphs")
            .WithOpenApi();

        return app;
    }

    private static Task<IResult> SuggestGraphs(
        HttpContext context,
        GraphSuggestRequest request,
        GraphSuggestionEngine suggestionEngine,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var schema = new QueryResultSchema
        {
            Columns = request.Columns.ToList(),
            RowCount = request.RowCount
        };
        var suggestions = GraphSuggestionEngine.SuggestFromSchema(schema);

        return Task.FromResult(Results.Ok(suggestions));
    }

    private static Task<IResult> RenderGraph(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var vegaLiteSpec = new
        {
            schema = "https://vega.github.io/schema/vega-lite/v5.json",
            description = "Generated graph spec",
            mark = "line",
            encoding = new { }
        };

        return Task.FromResult(Results.Ok(vegaLiteSpec));
    }

    private static Task<IResult> GetPrebuiltGraphs(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var prebuilt = new[]
        {
            new { name = "error_rate_by_hour", type = "timeseries" },
            new { name = "log_level_distribution", type = "bar" },
            new { name = "top_errors", type = "table" }
        };

        return Task.FromResult(Results.Ok(prebuilt));
    }
}
