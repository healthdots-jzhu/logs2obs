namespace Logs2Obs.Core.Handlers;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Models;
using MediatR;
using Microsoft.Extensions.Logging;

public class StartReplayHandler(
    IReplayService replayService,
    ILogger<StartReplayHandler> logger)
    : IRequestHandler<StartReplayCommand, ReplayJob>
{
    private readonly IReplayService _replayService = replayService;
    private readonly ILogger<StartReplayHandler> _logger = logger;

    public async Task<ReplayJob> Handle(StartReplayCommand command, CancellationToken ct)
    {
        // TODO Phase 9: delegate to IReplayService
        _logger.LogInformation("Starting replay for tenant {TenantId} from {From} to {To}",
            command.TenantId, command.From, command.To);
        return await _replayService.StartAsync(command.TenantId, command.From, command.To, command.Options, ct);
    }
}
