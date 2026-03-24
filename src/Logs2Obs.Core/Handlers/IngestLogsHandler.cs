namespace Logs2Obs.Core.Handlers;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using MediatR;
using Microsoft.Extensions.Logging;

public class IngestLogsHandler(
    IMessageBus messageBus,
    IIdempotencyStore idempotencyStore,
    ILogger<IngestLogsHandler> logger)
    : IRequestHandler<IngestLogsCommand, IngestLogsResult>
{
    private readonly IMessageBus _messageBus = messageBus;
    private readonly IIdempotencyStore _idempotencyStore = idempotencyStore;
    private readonly ILogger<IngestLogsHandler> _logger = logger;

    public async Task<IngestLogsResult> Handle(IngestLogsCommand command, CancellationToken ct)
    {
        // TODO Phase 4: validate entries, check idempotency, publish to IMessageBus
        _logger.LogInformation("Ingesting {Count} entries for tenant {TenantId}",
            command.Entries.Count, command.TenantId);
        await Task.CompletedTask;
        return new IngestLogsResult(0, 0, Guid.CreateVersion7().ToString("N"));
    }
}
