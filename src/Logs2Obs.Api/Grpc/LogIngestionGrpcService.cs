using Grpc.Core;
using Logs2Obs.Api.Grpc;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Logs2Obs.Api.Grpc;

public sealed class LogIngestionGrpcService : LogIngestionService.LogIngestionServiceBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<LogIngestionGrpcService> _logger;

    public LogIngestionGrpcService(IMediator mediator, ILogger<LogIngestionGrpcService> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public override async Task<IngestLogResponse> IngestLog(
        IngestLogRequest request,
        ServerCallContext context)
    {
        var tenantId = ExtractTenantId(request, context);
        var entries = request.Entries.Select(MapToDto).ToList();

        var command = new IngestLogsCommand
        {
            Entries = entries,
            TenantId = tenantId
        };
        var result = await _mediator.Send(command, context.CancellationToken);

        _logger.LogInformation(
            "gRPC IngestLog: TenantId={TenantId}, Accepted={Accepted}, Rejected={Rejected}",
            tenantId, result.Accepted, result.Rejected);

        return new IngestLogResponse
        {
            Accepted = result.Accepted,
            Rejected = result.Rejected,
            RequestId = Guid.NewGuid().ToString()
        };
    }

    public override async Task<IngestLogResponse> IngestLogStream(
        IAsyncStreamReader<IngestLogRequest> requestStream,
        ServerCallContext context)
    {
        var allEntries = new List<LogEntryDto>();
        string? tenantId = null;

        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
        {
            tenantId ??= ExtractTenantId(request, context);
            allEntries.AddRange(request.Entries.Select(MapToDto));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing tenant ID"));
        }

        var command = new IngestLogsCommand
        {
            Entries = allEntries,
            TenantId = tenantId
        };
        var result = await _mediator.Send(command, context.CancellationToken);

        _logger.LogInformation(
            "gRPC IngestLogStream: TenantId={TenantId}, Accepted={Accepted}, Rejected={Rejected}",
            tenantId, result.Accepted, result.Rejected);

        return new IngestLogResponse
        {
            Accepted = result.Accepted,
            Rejected = result.Rejected,
            RequestId = Guid.NewGuid().ToString()
        };
    }

    public override async Task IngestBidirectional(
        IAsyncStreamReader<IngestLogRequest> requestStream,
        IServerStreamWriter<IngestLogResponse> responseStream,
        ServerCallContext context)
    {
        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
        {
            var tenantId = ExtractTenantId(request, context);
            var entries = request.Entries.Select(MapToDto).ToList();

            var command = new IngestLogsCommand
            {
                Entries = entries,
                TenantId = tenantId
            };
            var result = await _mediator.Send(command, context.CancellationToken);

            await responseStream.WriteAsync(new IngestLogResponse
            {
                Accepted = result.Accepted,
                Rejected = result.Rejected,
                RequestId = Guid.NewGuid().ToString()
            }, context.CancellationToken);

            _logger.LogDebug(
                "gRPC IngestBidirectional batch: TenantId={TenantId}, Accepted={Accepted}",
                tenantId, result.Accepted);
        }
    }

    private static string ExtractTenantId(IngestLogRequest request, ServerCallContext context)
    {
        var metadataTenantId = context.RequestHeaders.GetValue("x-tenant-id");
        return metadataTenantId ?? request.TenantId ?? throw new RpcException(
            new Status(StatusCode.InvalidArgument, "Missing tenant ID"));
    }

    private static LogEntryDto MapToDto(LogEntryProto proto)
    {
        return new LogEntryDto
        {
            SourceId = proto.SourceId,
            LogType = proto.LogType,
            Level = proto.Level,
            Environment = proto.Environment,
            Category = string.IsNullOrWhiteSpace(proto.Category) ? null : proto.Category,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(proto.TimestampUnixMs),
            Message = proto.Message,
            TraceId = string.IsNullOrWhiteSpace(proto.TraceId) ? null : proto.TraceId,
            StackTrace = string.IsNullOrWhiteSpace(proto.StackTrace) ? null : proto.StackTrace,
            Tags = proto.Tags.Count > 0 ? proto.Tags.ToDictionary(kv => kv.Key, kv => kv.Value) : null
        };
    }
}
