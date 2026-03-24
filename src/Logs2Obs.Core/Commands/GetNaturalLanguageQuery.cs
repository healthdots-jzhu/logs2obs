namespace Logs2Obs.Core.Commands;

using Logs2Obs.Core.Abstractions;
using MediatR;

public sealed record GetNaturalLanguageQuery : IRequest<AiSqlResult>
{
    public required string TenantId { get; init; }
    public required string NaturalLanguage { get; init; }
}
