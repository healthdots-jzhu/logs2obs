namespace Logs2Obs.Adapters.Local.Search;

using System.Text.Json.Nodes;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Resilience;
using Logs2Obs.Adapters.Local.Options;
using Meilisearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CoreLogLevel = Logs2Obs.Core.Models.LogLevel;
using CoreLogType = Logs2Obs.Core.Models.LogType;
using CoreLogEntry = Logs2Obs.Core.Models.LogEntry;
using CoreIngestionMode = Logs2Obs.Core.Models.IngestionMode;

public sealed class MeilisearchIndexer(
    MeilisearchClient client,
    IOptions<MeilisearchOptions> options,
    ILogger<MeilisearchIndexer> logger) : ISearchIndexer
{
    private readonly MeilisearchOptions _opts = options.Value;

    private Index GetIndex() => client.Index(_opts.IndexName);

    public async Task IndexBatchAsync(IReadOnlyList<CoreLogEntry> entries, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var docs = entries.Select(e => new
            {
                id = e.Id,
                tenantId = e.TenantId,
                message = e.Message,
                level = e.Level.ToString(),
                logType = e.LogType.ToString(),
                timestampUnixMs = e.TimestampUnixMs,
                tags = e.Tags
            }).ToList();

            await GetIndex().AddDocumentsAsync(docs, cancellationToken: token);
            return true;
        }, ct);
        logger.LogDebug("Indexed {Count} documents to Meilisearch index {Index}", entries.Count, _opts.IndexName);
    }

    public async Task<IReadOnlyList<CoreLogEntry>> SearchAsync(string tenantId, string query, int limit, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForSearch<IReadOnlyList<CoreLogEntry>>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var searchQuery = new SearchQuery
            {
                Filter = $"tenantId = \"{tenantId}\"",
                Limit = limit
            };
            var result = await GetIndex().SearchAsync<JsonObject>(query, searchQuery, token);
            var entries = result.Hits.Select(hit => MapToLogEntry(hit)).ToList();
            return (IReadOnlyList<CoreLogEntry>)entries;
        }, ct);
    }

    public async Task<SearchAggResult> AggregateAsync(SearchAggRequest request, CancellationToken ct = default)
    {
        var all = await SearchAsync(request.TenantId, request.Filter ?? string.Empty, 10_000, ct);

        var buckets = all
            .Where(e => e.Tags is not null && e.Tags.ContainsKey(request.Field))
            .GroupBy(e => e.Tags![request.Field])
            .Select(g => new AggBucket { Key = g.Key, Count = g.LongCount() })
            .OrderByDescending(b => b.Count)
            .Take(request.Size)
            .ToList();

        return new SearchAggResult { Buckets = buckets };
    }

    public async Task DeleteByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            // Meilisearch v0.x doesn't have DeleteDocumentsByFilterAsync
            // We'll search and delete matching documents
            var searchQuery = new SearchQuery { Filter = $"tenantId = \"{tenantId}\"", Limit = 10000 };
            var results = await GetIndex().SearchAsync<JsonObject>(string.Empty, searchQuery, token);
            var ids = results.Hits.Select(h => h["id"]?.GetValue<string>() ?? string.Empty).Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (ids.Count > 0)
            {
                await GetIndex().DeleteDocumentsAsync(ids, token);
            }
            return true;
        }, ct);
        logger.LogInformation("Deleted all documents for tenant {TenantId} from Meilisearch", tenantId);
    }

    private static CoreLogEntry MapToLogEntry(JsonObject hit)
    {
        return new CoreLogEntry
        {
            Id = hit["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            TenantId = hit["tenantId"]?.GetValue<string>() ?? string.Empty,
            Message = hit["message"]?.GetValue<string>() ?? string.Empty,
            Level = Enum.TryParse<CoreLogLevel>(hit["level"]?.GetValue<string>(), out var level) ? level : CoreLogLevel.Information,
            LogType = Enum.TryParse<CoreLogType>(hit["logType"]?.GetValue<string>(), out var logType) ? logType : CoreLogType.Application,
            TimestampUnixMs = hit["timestampUnixMs"]?.GetValue<long>() ?? 0L,
            SourceId = string.Empty,
            Environment = string.Empty,
            IngestedAt = DateTimeOffset.UtcNow,
            IngestionMode = CoreIngestionMode.Push,
            SchemaVersion = 1
        };
    }
}
