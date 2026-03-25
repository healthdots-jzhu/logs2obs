using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Models;
using Logs2Obs.Core.Abstractions;

namespace Logs2Obs.Api.Endpoints;

public static class PullJobsEndpoints
{
    public static IEndpointRouteBuilder MapPullJobsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pull-jobs")
            .RequireAuthorization()
            .RequireRateLimiting("tenant-query");

        group.MapGet("", ListPullJobs)
            .WithName("ListPullJobs")
            .WithOpenApi();

        group.MapPost("", CreatePullJob)
            .WithName("CreatePullJob")
            .WithOpenApi();

        group.MapPut("/{jobId}", UpdatePullJob)
            .WithName("UpdatePullJob")
            .WithOpenApi();

        group.MapDelete("/{jobId}", DeletePullJob)
            .WithName("DeletePullJob")
            .WithOpenApi();

        group.MapPost("/{jobId}/run", RunPullJob)
            .WithName("RunPullJob")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> ListPullJobs(
        HttpContext context,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var jobs = new List<object>();
        
        await foreach (var job in metadataStore.QueryAsync<Dictionary<string, string>>("pull_jobs", j => true, cancellationToken))
        {
            jobs.Add(job);
        }

        return Results.Ok(jobs);
    }

    private static async Task<IResult> CreatePullJob(
        HttpContext context,
        CreatePullJobRequest request,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var jobId = Guid.NewGuid().ToString();

        var job = new Dictionary<string, string>
        {
            ["jobId"] = jobId,
            ["tenantId"] = tenantId,
            ["name"] = request.Name,
            ["sourceType"] = request.SourceType,
            ["schedule"] = request.Schedule,
            ["configuration"] = System.Text.Json.JsonSerializer.Serialize(request.Configuration),
            ["createdAt"] = DateTime.UtcNow.ToString("O"),
            ["isActive"] = "true"
        };

        await metadataStore.PutAsync("pull_jobs", job, cancellationToken);

        return Results.Ok(new { jobId, name = request.Name });
    }

    private static async Task<IResult> UpdatePullJob(
        HttpContext context,
        string jobId,
        UpdatePullJobRequest request,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var existing = await metadataStore.GetAsync<Dictionary<string, string>>("pull_jobs", jobId, cancellationToken);

        if (existing == null)
        {
            return Results.NotFound();
        }

        if (request.Name != null) existing["name"] = request.Name;
        if (request.Schedule != null) existing["schedule"] = request.Schedule;
        if (request.Configuration != null) existing["configuration"] = System.Text.Json.JsonSerializer.Serialize(request.Configuration);
        if (request.IsActive.HasValue) existing["isActive"] = request.IsActive.Value.ToString();
        existing["updatedAt"] = DateTime.UtcNow.ToString("O");

        await metadataStore.PutAsync("pull_jobs", existing, cancellationToken);

        return Results.Ok(new { jobId, updated = true });
    }

    private static async Task<IResult> DeletePullJob(
        HttpContext context,
        string jobId,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        await metadataStore.DeleteAsync("pull_jobs", jobId, cancellationToken);

        return Results.Ok(new { jobId, deleted = true });
    }

    private static Task<IResult> RunPullJob(
        HttpContext context,
        string jobId,
        IScheduler scheduler,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        // Triggering is not supported by IScheduler interface - would need background job queue
        return Task.FromResult(Results.Ok(new { jobId, triggered = false, message = "Manual triggering not yet implemented" }));
    }
}
