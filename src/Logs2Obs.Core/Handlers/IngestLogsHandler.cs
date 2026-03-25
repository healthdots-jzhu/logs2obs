namespace Logs2Obs.Core.Handlers;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Mapping;
using Logs2Obs.Core.Models;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

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
        var batchId = Guid.CreateVersion7().ToString("N");
        _logger.LogInformation("Ingesting {Count} entries for tenant {TenantId}, batchId {BatchId}",
            command.Entries.Count, command.TenantId, batchId);

        var sw = Stopwatch.StartNew();
        var validEntries = new List<LogEntry>();
        var duplicateCount = 0;

        foreach (var dto in command.Entries)
        {
            try
            {
                var entry = DtoMapper.ToDomain(dto, command.TenantId, command.IngestionMode);
                
                var idempotencyKey = $"ingest:{entry.Id}";
                var isNew = await _idempotencyStore.CheckAndSetAsync(
                    idempotencyKey,
                    TimeSpan.FromHours(24),
                    ct);

                if (!isNew)
                {
                    duplicateCount++;
                    _logger.LogDebug("Duplicate entry {EntryId} skipped", entry.Id);
                    continue;
                }

                validEntries.Add(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to map entry for tenant {TenantId}", command.TenantId);
            }
        }

        if (validEntries.Count == 0)
        {
            _logger.LogWarning("No valid entries to ingest for tenant {TenantId}", command.TenantId);
            return new IngestLogsResult(0, command.Entries.Count - duplicateCount, batchId);
        }

        var batch = validEntries.AsReadOnly();

        await Task.WhenAll(
            _messageBus.PublishAsync("ls-storage-writer", batch, ct),
            _messageBus.PublishAsync("ls-search-indexer", batch, ct)
        );

        _logger.LogInformation("Published {Count} entries to queues in {ElapsedMs}ms, batchId {BatchId}",
            validEntries.Count, sw.ElapsedMilliseconds, batchId);

        return new IngestLogsResult(validEntries.Count, command.Entries.Count - validEntries.Count - duplicateCount, batchId);
    }
}
