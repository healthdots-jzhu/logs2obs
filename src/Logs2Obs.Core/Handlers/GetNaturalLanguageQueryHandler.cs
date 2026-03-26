namespace Logs2Obs.Core.Handlers;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Exceptions;
using Logs2Obs.Core.Models;
using MediatR;
using Microsoft.Extensions.Logging;

public class GetNaturalLanguageQueryHandler(
    IAiService aiService,
    ISqlSafetyValidator safetyValidator,
    ILogger<GetNaturalLanguageQueryHandler> logger)
    : IRequestHandler<GetNaturalLanguageQuery, AiSqlResult>
{
    private readonly IAiService _aiService = aiService;
    private readonly ISqlSafetyValidator _safetyValidator = safetyValidator;
    private readonly ILogger<GetNaturalLanguageQueryHandler> _logger = logger;

    public async Task<AiSqlResult> Handle(GetNaturalLanguageQuery command, CancellationToken ct)
    {
        _logger.LogInformation("Translating NL query for tenant {TenantId}", command.TenantId);

        var context = new QueryContext { TenantId = command.TenantId };
        var result = await _aiService.TranslateToSqlAsync(command.NaturalLanguage, context, ct);

        var safetyReport = _safetyValidator.Analyze(result.Sql);
        if (safetyReport.Errors.Count > 0)
            throw new SqlSafetyException(string.Join("; ", safetyReport.Errors));
        if (safetyReport.Warnings.Count > 0)
            _logger.LogWarning("AI SQL safety warnings: {Warnings}", string.Join("; ", safetyReport.Warnings));

        return new AiSqlResult
        {
            Sql = result.Sql,
            Explanation = result.Explanation,
            SuggestedGraphType = result.SuggestedGraphType,
            InputTokenCount = 0,
            OutputTokenCount = 0,
            ModelUsed = "unknown"
        };
    }
}
