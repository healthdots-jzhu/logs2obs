namespace Logs2Obs.QueryEngine.Replay;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Resilience;
using Logs2Obs.QueryEngine.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

public sealed class ReplayService(
    IObjectStore objectStore,
    IMessageBus messageBus,
    IMetadataStore metadataStore,
    IOptions<QueryEngineOptions> options,
    ILogger<ReplayService> logger) : IReplayService
{
    private const string TableName = "replay-jobs";

    private readonly IMessageBus _messageBus = messageBus;
    private readonly IMetadataStore _metadataStore = metadataStore;
    private readonly QueryEngineOptions _options = options.Value;
    private readonly ILogger<ReplayService> _logger = logger;
    private readonly ResiliencePipeline<object?> _publishPipeline = ResiliencePipelines.ForExternalIo<object?>();

    public Task<ReplayJob?> GetStatusAsync(string jobId, CancellationToken ct = default) =>
        _metadataStore.GetAsync<ReplayJob>(TableName, jobId, ct);

    public async Task<ReplayJob> StartAsync(string tenantId, DateTimeOffset from, DateTimeOffset to, ReplayOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(objectStore);

        var job = new ReplayJob
        {
            JobId = Guid.CreateVersion7().ToString("N"),
            TenantId = tenantId,
            From = from,
            To = to,
            Options = options,
            Status = ReplayStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _metadataStore.PutAsync(TableName, job, ct);

        var evt = new ReplayStartedEvent
        {
            JobId = job.JobId,
            TenantId = tenantId,
            From = from,
            To = to,
            Options = options
        };

        await _publishPipeline.ExecuteAsync(async token =>
        {
            await _messageBus.PublishAsync(_options.SystemEventsQueue, evt, token);
            return (object?)null;
        }, ct);

        _logger.LogInformation("Replay job {JobId} queued for tenant {TenantId}", job.JobId, tenantId);
        return job;
    }

    public async Task CancelAsync(string jobId, CancellationToken ct = default)
    {
        var job = await _metadataStore.GetAsync<ReplayJob>(TableName, jobId, ct);
        if (job is null)
            return;

        var updated = job with { Status = ReplayStatus.Failed };
        await _metadataStore.PutAsync(TableName, updated, ct);
        _logger.LogWarning("Replay job {JobId} cancelled", jobId);
    }
}
