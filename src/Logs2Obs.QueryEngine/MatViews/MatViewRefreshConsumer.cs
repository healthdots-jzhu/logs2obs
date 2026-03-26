namespace Logs2Obs.QueryEngine.MatViews;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.QueryEngine.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class MatViewRefreshConsumer(
    IMessageBus messageBus,
    MatViewRefreshService refreshService,
    IMatViewEngine matViewEngine,
    IOptions<QueryEngineOptions> options,
    ILogger<MatViewRefreshConsumer> logger) : BackgroundService
{
    private readonly IMessageBus _messageBus = messageBus;
    private readonly MatViewRefreshService _refreshService = refreshService;
    private readonly IMatViewEngine _matViewEngine = matViewEngine;
    private readonly QueryEngineOptions _options = options.Value;
    private readonly ILogger<MatViewRefreshConsumer> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("MatViewRefreshConsumer starting on queue {Queue}", _options.MatViewRefreshQueue);
        _logger.LogDebug("MatView engine active: {EngineType}", _matViewEngine.GetType().Name);

        await foreach (var envelope in _messageBus.SubscribeAsync<MatViewRefreshRequest>(_options.MatViewRefreshQueue, ct))
        {
            try
            {
                await _refreshService.RefreshAllForTenantAsync(envelope.Payload.TenantId, ct);
                await _messageBus.AcknowledgeAsync(envelope.ReceiptHandle, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MatView refresh failed for tenant {TenantId}", envelope.Payload.TenantId);
                await _messageBus.DeadLetterAsync(envelope.ReceiptHandle, ex.Message, ct);
            }
        }
    }

    private sealed record MatViewRefreshRequest
    {
        public required string TenantId { get; init; }
    }
}
