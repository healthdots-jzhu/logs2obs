using Logs2Obs.Api.Auth;
using Logs2Obs.Api.Models;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Models;
using MediatR;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Logging;

namespace Logs2Obs.Api.Endpoints;

public static class LogsEndpoints
{
    public static IEndpointRouteBuilder MapLogsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/logs")
            .RequireAuthorization()
            .RequireRateLimiting("tenant-ingest");

        group.MapPost("", IngestLogs)
            .WithName("IngestLogs")
            .WithOpenApi()
            .WithRequestTimeout("IngestTimeout");

        group.MapPost("/bulk", IngestBulk)
            .WithName("IngestBulk")
            .WithOpenApi()
            .DisableAntiforgery()
            .WithRequestTimeout("IngestTimeout");

        group.MapPost("/metrics", IngestMetrics)
            .WithName("IngestMetrics")
            .WithOpenApi()
            .WithRequestTimeout("IngestTimeout");

        return app;
    }

    private static async Task<IResult> IngestLogs(
        HttpContext context,
        IngestLogsRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var command = new IngestLogsCommand
        {
            Entries = request.Entries,
            TenantId = tenantId
        };
        var result = await mediator.Send(command, cancellationToken);

        return Results.Ok(new
        {
            accepted = result.Accepted,
            rejected = result.Rejected,
            requestId = result.BatchId
        });
    }

    private static async Task<IResult> IngestBulk(
        HttpContext context,
        IFormFile file,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();

        if (file.Length == 0)
        {
            return Results.BadRequest(new { error = "Empty file" });
        }

        var entries = new List<LogEntryDto>();
        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var entry = System.Text.Json.JsonSerializer.Deserialize<LogEntryDto>(line);
                if (entry != null) entries.Add(entry);
            }
            catch
            {
                // Skip invalid lines
            }
        }

        var command = new IngestLogsCommand
        {
            Entries = entries,
            TenantId = tenantId
        };
        var result = await mediator.Send(command, cancellationToken);

        return Results.Ok(new
        {
            accepted = result.Accepted,
            rejected = result.Rejected,
            totalLines = entries.Count,
            requestId = result.BatchId
        });
    }

    private static async Task<IResult> IngestMetrics(
        HttpContext context,
        IngestLogsRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var tenantId = context.GetTenantId();
        var command = new IngestLogsCommand
        {
            Entries = request.Entries,
            TenantId = tenantId
        };
        var result = await mediator.Send(command, cancellationToken);

        return Results.Ok(new
        {
            accepted = result.Accepted,
            rejected = result.Rejected,
            requestId = result.BatchId
        });
    }
}
