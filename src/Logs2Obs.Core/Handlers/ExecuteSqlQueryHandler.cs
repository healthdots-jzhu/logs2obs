namespace Logs2Obs.Core.Handlers;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Models;
using MediatR;
using Microsoft.Extensions.Logging;

public class ExecuteSqlQueryHandler(
    IQueryEngine queryEngine,
    ISqlSafetyValidator sqlSafetyValidator,
    ILogger<ExecuteSqlQueryHandler> logger)
    : IRequestHandler<ExecuteSqlQuery, QuerySubmitResult>
{
    private readonly IQueryEngine _queryEngine = queryEngine;
    private readonly ISqlSafetyValidator _sqlSafetyValidator = sqlSafetyValidator;
    private readonly ILogger<ExecuteSqlQueryHandler> _logger = logger;

    public async Task<QuerySubmitResult> Handle(ExecuteSqlQuery command, CancellationToken ct)
    {
        // TODO Phase 7: validate SQL safety, route to tier, estimate cost, execute
        _logger.LogInformation("Executing SQL query for tenant {TenantId}", command.TenantId);
        await Task.CompletedTask;
        return new QuerySubmitResult
        {
            ExecutionId    = Guid.CreateVersion7().ToString("N"),
            Status         = QueryStatus.Pending,
            Estimate       = null,
            ResultLocation = null
        };
    }
}
