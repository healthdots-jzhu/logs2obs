namespace Logs2Obs.Core.Commands;

using Logs2Obs.Core.Models;
using MediatR;

public sealed record IngestLogsCommand : IRequest<IngestLogsResult>
{
    public required string TenantId { get; init; }
    public required IReadOnlyList<LogEntryDto> Entries { get; init; }
    public IngestionMode IngestionMode { get; init; } = IngestionMode.Push;
}
