using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Models;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using MediatR;
using Microsoft.AspNetCore.Http.Timeouts;

namespace Logs2Obs.Api.Endpoints;

public static class QueryEndpoints
{
    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/query")
            .RequireAuthorization()
            .RequireRateLimiting("tenant-query");

        group.MapPost("/sql", ExecuteSqlQuery)
            .WithName("ExecuteSqlQuery")
            .WithOpenApi()
            .WithRequestTimeout("QueryTimeout");

        group.MapGet("/{queryId}/status", GetQueryStatus)
            .WithName("GetQueryStatus")
            .WithOpenApi()
            .WithRequestTimeout("QueryTimeout");

        group.MapGet("/{queryId}/results", GetQueryResults)
            .WithName("GetQueryResults")
            .WithOpenApi()
            .WithRequestTimeout("QueryTimeout");

        group.MapPost("/search", SearchLogs)
            .WithName("SearchLogs")
            .WithOpenApi()
            .WithRequestTimeout("QueryTimeout");

        group.MapPost("/natural", NaturalLanguageQuery)
            .WithName("NaturalLanguageQuery")
            .WithOpenApi()
            .WithRequestTimeout("QueryTimeout");

        group.MapGet("/saved", ListSavedQueries)
            .WithName("ListSavedQueries")
            .WithOpenApi()
            .WithRequestTimeout("QueryTimeout");

        group.MapPost("/saved", SaveQuery)
            .WithName("SaveQuery")
            .WithOpenApi()
            .WithRequestTimeout("QueryTimeout");

        group.MapPost("/saved/{id}/run", RunSavedQuery)
            .WithName("RunSavedQuery")
            .WithOpenApi()
            .WithRequestTimeout("QueryTimeout");

        return app;
    }

    private static async Task<IResult> ExecuteSqlQuery(
        HttpContext context,
        SqlQueryRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var query = new ExecuteSqlQuery
        {
            Sql = request.Sql,
            TenantId = tenantId
        };
        var result = await mediator.Send(query, cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetQueryStatus(
        HttpContext context,
        string queryId,
        IQueryEngine queryEngine,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var result = await queryEngine.GetResultAsync(queryId, cancellationToken);

        return result != null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> GetQueryResults(
        HttpContext context,
        string queryId,
        IQueryEngine queryEngine,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var result = await queryEngine.GetResultAsync(queryId, cancellationToken);

        return result != null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> SearchLogs(
        HttpContext context,
        SearchRequest request,
        ISearchIndexer searchIndexer,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var results = await searchIndexer.SearchAsync(
            tenantId,
            request.Query,
            request.Limit,
            cancellationToken);

        return Results.Ok(results);
    }

    private static async Task<IResult> NaturalLanguageQuery(
        HttpContext context,
        NaturalLanguageRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var query = new GetNaturalLanguageQuery
        {
            NaturalLanguage = request.Question,
            TenantId = tenantId
        };
        var result = await mediator.Send(query, cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> ListSavedQueries(
        HttpContext context,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var queries = new List<object>();
        
        await foreach (var q in metadataStore.QueryAsync<Dictionary<string, string>>("saved_queries", _ => true, cancellationToken))
        {
            queries.Add(q);
        }

        return Results.Ok(queries);
    }

    private static async Task<IResult> SaveQuery(
        HttpContext context,
        SaveQueryRequest request,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var queryId = Guid.NewGuid().ToString();
        
        var query = new Dictionary<string, string>
        {
            ["queryId"] = queryId,
            ["tenantId"] = tenantId,
            ["name"] = request.Name,
            ["sql"] = request.Sql,
            ["description"] = request.Description ?? "",
            ["createdAt"] = DateTime.UtcNow.ToString("O")
        };

        await metadataStore.PutAsync("saved_queries", query, cancellationToken);

        return Results.Ok(new { queryId, name = request.Name });
    }

    private static async Task<IResult> RunSavedQuery(
        HttpContext context,
        string id,
        RunSavedQueryRequest request,
        IMetadataStore metadataStore,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var query = await metadataStore.GetAsync<Dictionary<string, string>>("saved_queries", id, cancellationToken);

        if (query == null || !query.TryGetValue("sql", out var sql))
        {
            return Results.NotFound();
        }

        var command = new ExecuteSqlQuery
        {
            Sql = sql,
            TenantId = tenantId
        };
        var result = await mediator.Send(command, cancellationToken);

        return Results.Ok(result);
    }
}
