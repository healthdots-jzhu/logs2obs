using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Models;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Models;
using MediatR;

namespace Logs2Obs.Api.Endpoints;

public static class ReplayEndpoints
{
    public static IEndpointRouteBuilder MapReplayEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/replay")
            .RequireAuthorization()
            .RequireRateLimiting("tenant-query");

        group.MapPost("", StartReplay)
            .WithName("StartReplay")
            .WithOpenApi();

        group.MapGet("/{jobId}", GetReplayStatus)
            .WithName("GetReplayStatus")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> StartReplay(
        HttpContext context,
        StartReplayRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var command = new StartReplayCommand
        {
            TenantId = tenantId,
            From = DateTime.Parse(request.StartTime),
            To = DateTime.Parse(request.EndTime),
            Options = new ReplayOptions()
        };

        var job = await mediator.Send(command, cancellationToken);

        return Results.Ok(new { jobId = job.JobId, status = "started" });
    }

    private static async Task<IResult> GetReplayStatus(
        HttpContext context,
        string jobId,
        IMetadataStore metadataStore,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var metadata = await metadataStore.GetAsync<Dictionary<string, string>>("replay_jobs", jobId, cancellationToken);

        if (metadata == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(metadata);
    }
}
