namespace Logs2Obs.Core.Handlers;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Models;
using MediatR;
using Microsoft.Extensions.Logging;

public class GetNaturalLanguageQueryHandler(
    IAiService aiService,
    ILogger<GetNaturalLanguageQueryHandler> logger)
    : IRequestHandler<GetNaturalLanguageQuery, AiSqlResult>
{
    private readonly IAiService _aiService = aiService;
    private readonly ILogger<GetNaturalLanguageQueryHandler> _logger = logger;

    public async Task<AiSqlResult> Handle(GetNaturalLanguageQuery command, CancellationToken ct)
    {
        // TODO Phase 8: build schema context, call IAiService, validate safety, audit log
        _logger.LogInformation("Translating NL query for tenant {TenantId}", command.TenantId);
        await Task.CompletedTask;
        return new AiSqlResult
        {
            Sql                = "SELECT 1",
            Explanation        = "Placeholder — not yet implemented",
            SuggestedGraphType = GraphType.LineChart,
            InputTokenCount    = 0,
            OutputTokenCount   = 0,
            ModelUsed          = "none"
        };
    }
}
