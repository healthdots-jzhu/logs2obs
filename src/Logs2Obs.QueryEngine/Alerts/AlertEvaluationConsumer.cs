namespace Logs2Obs.QueryEngine.Alerts;

using System.Diagnostics;
using System.Text.Json;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Resilience;
using Logs2Obs.QueryEngine.Models;
using Logs2Obs.QueryEngine.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

public sealed class AlertEvaluationConsumer(
    IMessageBus messageBus,
    IMetadataStore metadataStore,
    IQueryEngine queryEngine,
    AlertEvaluationMetrics metrics,
    IOptions<QueryEngineOptions> options,
    ILogger<AlertEvaluationConsumer> logger) : BackgroundService
{
    private const string AlertRulesTable = "alert-rules";
    private const int MaxRuleParallelism = 4;

    private readonly IMessageBus _messageBus = messageBus;
    private readonly IMetadataStore _metadataStore = metadataStore;
    private readonly IQueryEngine _queryEngine = queryEngine;
    private readonly AlertEvaluationMetrics _metrics = metrics;
    private readonly QueryEngineOptions _options = options.Value;
    private readonly ILogger<AlertEvaluationConsumer> _logger = logger;
    private readonly ResiliencePipeline<QuerySubmitResult> _queryPipeline =
        new ResiliencePipelineBuilder<QuerySubmitResult>()
            .AddRetry(new RetryStrategyOptions<QuerySubmitResult>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200)
            })
            .Build();
    private readonly ResiliencePipeline<object?> _publishPipeline = ResiliencePipelines.ForExternalIo<object?>();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("AlertEvaluationConsumer starting on queue {Queue}", _options.AlertEvaluatorQueue);

        await foreach (var envelope in _messageBus.SubscribeAsync<LogEntryBatch>(_options.AlertEvaluatorQueue, ct))
        {
            var batch = envelope.Payload;
            try
            {
                var rules = await GetEnabledRulesAsync(batch.TenantId, ct);
                if (rules.Count == 0)
                {
                    await _messageBus.AcknowledgeAsync(envelope.ReceiptHandle, ct);
                    continue;
                }

                await Parallel.ForEachAsync(
                    rules,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MaxRuleParallelism,
                        CancellationToken = ct
                    },
                    async (rule, token) =>
                    {
                        var stopwatch = Stopwatch.StartNew();
                        try
                        {
                            var result = await _queryPipeline.ExecuteAsync(
                                async t => await _queryEngine.SubmitAsync(batch.TenantId, rule.Sql, t),
                                token);

                            if (result.Status != QueryStatus.Completed)
                            {
                                _metrics.RecordError(batch.TenantId, rule.RuleId, $"query_{result.Status}");
                                return;
                            }

                            var value = TryExtractNumericValue(result);
                            if (!value.HasValue)
                            {
                                _metrics.RecordError(batch.TenantId, rule.RuleId, "no_result");
                                return;
                            }

                            _metrics.RecordEvaluated(batch.TenantId, rule.RuleId);

                            if (!IsThresholdBreached(value.Value, rule.ThresholdValue, rule.ThresholdOperator))
                                return;

                            var evt = new AlertFiredEvent
                            {
                                EventId = Guid.NewGuid().ToString("N"),
                                RuleId = rule.RuleId,
                                TenantId = rule.TenantId,
                                RuleName = rule.Name,
                                ActualValue = value.Value,
                                ThresholdValue = rule.ThresholdValue,
                                ThresholdOperator = rule.ThresholdOperator,
                                NotificationChannel = rule.NotificationChannel,
                                Destinations = rule.Destinations
                            };

                            await _publishPipeline.ExecuteAsync(async t =>
                            {
                                await _messageBus.PublishAsync(_options.SystemEventsQueue, evt, t);
                                return (object?)null;
                            }, token);

                            _metrics.RecordFired(batch.TenantId, rule.RuleId);
                        }
                        catch (Exception ex)
                        {
                            _metrics.RecordError(batch.TenantId, rule.RuleId, ex.GetType().Name);
                            _logger.LogError(ex, "Alert rule {RuleId} evaluation failed for tenant {TenantId}", rule.RuleId, batch.TenantId);
                        }
                        finally
                        {
                            stopwatch.Stop();
                            _metrics.RecordDuration(batch.TenantId, rule.RuleId, stopwatch.Elapsed.TotalMilliseconds);
                        }
                    });

                await _messageBus.AcknowledgeAsync(envelope.ReceiptHandle, ct);
            }
            catch (Exception ex)
            {
                _metrics.RecordError(batch.TenantId, "batch", ex.GetType().Name);
                _logger.LogError(ex, "Alert evaluation batch failed for tenant {TenantId}", batch.TenantId);
                await _messageBus.DeadLetterAsync(envelope.ReceiptHandle, ex.Message, ct);
            }
        }
    }

    private async Task<IReadOnlyList<AlertRule>> GetEnabledRulesAsync(string tenantId, CancellationToken ct)
    {
        var stored = await _metadataStore.GetAsync<IReadOnlyList<AlertRule>>(AlertRulesTable, BuildRulesKey(tenantId), ct);
        if (stored is not null)
        {
            return stored.Where(r => r.IsEnabled).ToList();
        }

        var results = new List<AlertRule>();
        await foreach (var rule in _metadataStore.QueryAsync<AlertRule>(AlertRulesTable, r => r.TenantId == tenantId && r.IsEnabled, ct))
        {
            results.Add(rule);
        }

        return results;
    }

    private static string BuildRulesKey(string tenantId) => $"alert-rules:{tenantId}";

    private static double? TryExtractNumericValue(QuerySubmitResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ResultLocation))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(result.ResultLocation);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var first = doc.RootElement[0];
            if (first.ValueKind == JsonValueKind.Object)
            {
                var enumerator = first.EnumerateObject();
                if (!enumerator.MoveNext())
                    return null;
                return TryGetDouble(enumerator.Current.Value);
            }

            return TryGetDouble(first);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? TryGetDouble(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static bool IsThresholdBreached(double actual, double threshold, string op)
    {
        var normalized = op.Trim();
        return normalized switch
        {
            ">" => actual > threshold,
            "<" => actual < threshold,
            ">=" => actual >= threshold,
            "<=" => actual <= threshold,
            "==" => Math.Abs(actual - threshold) < double.Epsilon,
            "=" => Math.Abs(actual - threshold) < double.Epsilon,
            _ => throw new InvalidOperationException($"Unsupported threshold operator '{op}'.")
        };
    }
}
