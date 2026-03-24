namespace Logs2Obs.Core.Commands;

using Logs2Obs.Core.Abstractions;
using MediatR;

public sealed record ExecuteSqlQuery : IRequest<QuerySubmitResult>
{
    public required string TenantId { get; init; }
    public required string Sql { get; init; }
    public double ConfirmCostIfAboveUsd { get; init; } = 0.05;
}
