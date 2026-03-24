namespace Logs2Obs.Core.Commands;

using Logs2Obs.Core.Models;
using MediatR;

public sealed record StartReplayCommand : IRequest<ReplayJob>
{
    public required string TenantId { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required ReplayOptions Options { get; init; }
}
