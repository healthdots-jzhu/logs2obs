namespace Logs2Obs.Adapters.Aws.Search;

using System.Text.Json;
using Logs2Obs.Adapters.Aws.Options;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Resilience;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using OpenSearch.Net;
using CoreIngestionMode = Logs2Obs.Core.Models.IngestionMode;
using CoreLogLevel = Logs2Obs.Core.Models.LogLevel;
using CoreLogType = Logs2Obs.Core.Models.LogType;

public sealed class OpenSearchIndexer(
    IOpenSearchClient client,
    IOptions<AwsAdaptersOptions> options) : ISearchIndexer, IDisposable
{
    private readonly OpenSearchOptions _opts = options.Value.OpenSearch;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public async Task IndexBatchAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
            return;

        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var operations = new List<object>(entries.Count * 2);
            foreach (var entry in entries)
            {
                var indexName = GetIndexName(entry.TenantId);
                operations.Add(new { index = new { _index = indexName, _id = entry.Id } });
                operations.Add(ToDocument(entry));
            }

            var response = await client.LowLevel.BulkAsync<StringResponse>(
                PostData.MultiJson(operations), null, token).ConfigureAwait(false);

            if (response.HttpStatusCode is >= 400)
                throw new InvalidOperationException($"OpenSearch bulk indexing failed with status {response.HttpStatusCode}.");

            return true;
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LogEntry>> SearchAsync(string tenantId, string query, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var pipeline = ResiliencePipelines.ForSearch<IReadOnlyList<LogEntry>>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var indexName = GetIndexName(tenantId);
            var body = new
            {
                size = limit,
                query = BuildQuery(tenantId, query, null, null)
            };

            var response = await client.LowLevel.SearchAsync<StringResponse>(
                indexName, PostData.Serializable(body), null, token).ConfigureAwait(false);

            if (response.HttpStatusCode is >= 400)
                throw new InvalidOperationException($"OpenSearch search failed with status {response.HttpStatusCode}.");

            return ParseSearchResults(response.Body);
        }, ct).ConfigureAwait(false);
    }

    public async Task<SearchAggResult> AggregateAsync(SearchAggRequest request, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var pipeline = ResiliencePipelines.ForSearch<SearchAggResult>();
        return await pipeline.ExecuteAsync(async token =>
        {
            var indexName = GetIndexName(request.TenantId);
            var fieldName = ResolveAggField(request.Field);
            var body = new
            {
                size = 0,
                query = BuildQuery(request.TenantId, request.Filter, request.From, request.To),
                aggs = new
                {
                    by_field = new
                    {
                        terms = new
                        {
                            field = fieldName,
                            size = request.Size
                        }
                    },
                    by_time = new
                    {
                        date_histogram = new
                        {
                            field = "timestamp",
                            calendar_interval = "hour"
                        }
                    }
                }
            };

            var response = await client.LowLevel.SearchAsync<StringResponse>(
                indexName, PostData.Serializable(body), null, token).ConfigureAwait(false);

            if (response.HttpStatusCode is >= 400)
                throw new InvalidOperationException($"OpenSearch aggregation failed with status {response.HttpStatusCode}.");

            var buckets = ParseAggBuckets(response.Body);
            return new SearchAggResult { Buckets = buckets };
        }, ct).ConfigureAwait(false);
    }

    public async Task DeleteByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var indexName = GetIndexName(tenantId);
            var body = new
            {
                query = new
                {
                    term = new
                    {
                        tenantId
                    }
                }
            };

            var response = await client.LowLevel.DeleteByQueryAsync<StringResponse>(
                indexName, PostData.Serializable(body), null, token).ConfigureAwait(false);

            if (response.HttpStatusCode is >= 400)
                throw new InvalidOperationException($"OpenSearch delete-by-tenant failed with status {response.HttpStatusCode}.");

            return true;
        }, ct).ConfigureAwait(false);
    }

    public void Dispose() => _initLock.Dispose();

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            await EnsurePolicyAsync(ct).ConfigureAwait(false);
            await EnsureTemplateAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task EnsurePolicyAsync(CancellationToken ct)
    {
        var policyBody = new
        {
            policy = new
            {
                description = "logs2obs hot tier policy",
                default_state = "hot",
                states = new[]
                {
                    new
                    {
                        name = "hot",
                        actions = Array.Empty<object>(),
                        transitions = Array.Empty<object>()
                    }
                }
            }
        };

        var response = await client.LowLevel.DoRequestAsync<StringResponse>(
            HttpMethod.PUT,
            $"/_plugins/_ism/policies/{_opts.IlmPolicyName}",
            ct,
            PostData.Serializable(policyBody)).ConfigureAwait(false);

        if (response.HttpStatusCode is >= 400 and not 409)
            throw new InvalidOperationException($"OpenSearch ISM policy create returned status {response.HttpStatusCode}.");
    }

    private async Task EnsureTemplateAsync(CancellationToken ct)
    {
        var templateName = $"{_opts.IndexPrefix}-template";
        var templateBody = new
        {
            index_patterns = new[] { $"{_opts.IndexPrefix}-*" },
            template = new
            {
                settings = new Dictionary<string, object>
                {
                    ["number_of_shards"] = _opts.NumberOfShards,
                    ["number_of_replicas"] = _opts.NumberOfReplicas,
                    ["index.plugins.index_state_management.policy_id"] = _opts.IlmPolicyName
                }
            }
        };

        var response = await client.LowLevel.DoRequestAsync<StringResponse>(
            HttpMethod.PUT,
            $"/_index_template/{templateName}",
            ct,
            PostData.Serializable(templateBody)).ConfigureAwait(false);

        if (response.HttpStatusCode is >= 400 and not 409)
            throw new InvalidOperationException($"OpenSearch index template create returned status {response.HttpStatusCode}.");
    }

    private string GetIndexName(string tenantId) => $"{_opts.IndexPrefix}-{tenantId}".ToLowerInvariant();

    private static Dictionary<string, object?> BuildQuery(string tenantId, string? query, DateTimeOffset? from, DateTimeOffset? to)
    {
        var filters = new List<object>
        {
            new { term = new { tenantId } }
        };

        if (from.HasValue || to.HasValue)
        {
            var range = new Dictionary<string, object>();
            if (from.HasValue)
                range["gte"] = from.Value.UtcDateTime.ToString("O");
            if (to.HasValue)
                range["lte"] = to.Value.UtcDateTime.ToString("O");

            filters.Add(new { range = new Dictionary<string, object> { ["timestamp"] = range } });
        }

        var boolQuery = new Dictionary<string, object?>
        {
            ["filter"] = filters
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            boolQuery["must"] = new[]
            {
                new
                {
                    query_string = new
                    {
                        query,
                        fields = new[] { "message", "category", "tags.*" },
                        analyze_wildcard = true
                    }
                }
            };
        }

        return new Dictionary<string, object?>
        {
            ["bool"] = boolQuery
        };
    }

    private static string ResolveAggField(string field)
    {
        if (field.Equals("category", StringComparison.OrdinalIgnoreCase)
            || field.Equals("level", StringComparison.OrdinalIgnoreCase)
            || field.Equals("logType", StringComparison.OrdinalIgnoreCase)
            || field.Equals("environment", StringComparison.OrdinalIgnoreCase))
        {
            return field;
        }

        return $"tags.{field}";
    }

    private static List<LogEntry> ParseSearchResults(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var hits = doc.RootElement.GetProperty("hits").GetProperty("hits");
        var results = new List<LogEntry>();
        foreach (var hit in hits.EnumerateArray())
        {
            if (!hit.TryGetProperty("_source", out var source))
                continue;

            var model = JsonSerializer.Deserialize<OpenSearchLogDocument>(source.GetRawText());
            if (model is null)
                continue;

            results.Add(FromDocument(model));
        }

        return results;
    }

    private static IReadOnlyList<AggBucket> ParseAggBuckets(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("aggregations", out var aggs))
            return Array.Empty<AggBucket>();
        if (!aggs.TryGetProperty("by_field", out var byField))
            return Array.Empty<AggBucket>();
        if (!byField.TryGetProperty("buckets", out var bucketsElement))
            return Array.Empty<AggBucket>();

        var buckets = new List<AggBucket>();
        foreach (var bucket in bucketsElement.EnumerateArray())
        {
            var key = bucket.GetProperty("key").ToString();
            var count = bucket.GetProperty("doc_count").GetInt64();
            buckets.Add(new AggBucket { Key = key, Count = count });
        }

        return buckets;
    }

    private static OpenSearchLogDocument ToDocument(LogEntry entry)
    {
        return new OpenSearchLogDocument
        {
            Id = entry.Id,
            TenantId = entry.TenantId,
            Message = entry.Message,
            Category = entry.Category,
            Level = entry.Level.ToString(),
            LogType = entry.LogType.ToString(),
            TimestampUnixMs = entry.TimestampUnixMs,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampUnixMs),
            SourceId = entry.SourceId,
            Environment = entry.Environment,
            TraceId = entry.TraceId,
            StackTrace = entry.StackTrace,
            Tags = entry.Tags?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            IngestedAt = entry.IngestedAt,
            IngestionMode = entry.IngestionMode.ToString(),
            SchemaVersion = entry.SchemaVersion
        };
    }

    private static LogEntry FromDocument(OpenSearchLogDocument doc)
    {
        var level = Enum.TryParse<CoreLogLevel>(doc.Level, true, out var parsedLevel)
            ? parsedLevel
            : CoreLogLevel.Information;
        var logType = Enum.TryParse<CoreLogType>(doc.LogType, true, out var parsedType)
            ? parsedType
            : CoreLogType.Application;
        var ingestionMode = Enum.TryParse<CoreIngestionMode>(doc.IngestionMode, true, out var parsedMode)
            ? parsedMode
            : CoreIngestionMode.Push;

        return new LogEntry
        {
            Id = doc.Id,
            TenantId = doc.TenantId,
            Message = doc.Message,
            Category = doc.Category,
            Level = level,
            LogType = logType,
            TimestampUnixMs = doc.TimestampUnixMs,
            SourceId = doc.SourceId,
            Environment = doc.Environment,
            TraceId = doc.TraceId,
            StackTrace = doc.StackTrace,
            Tags = doc.Tags,
            IngestedAt = doc.IngestedAt,
            IngestionMode = ingestionMode,
            SchemaVersion = doc.SchemaVersion
        };
    }

    private sealed record OpenSearchLogDocument
    {
        public required string Id { get; init; }
        public required string TenantId { get; init; }
        public required string Message { get; init; }
        public string? Category { get; init; }
        public required string Level { get; init; }
        public required string LogType { get; init; }
        public required long TimestampUnixMs { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
        public required string SourceId { get; init; }
        public required string Environment { get; init; }
        public string? TraceId { get; init; }
        public string? StackTrace { get; init; }
        public Dictionary<string, string>? Tags { get; init; }
        public required DateTimeOffset IngestedAt { get; init; }
        public required string IngestionMode { get; init; }
        public uint SchemaVersion { get; init; }
    }
}
