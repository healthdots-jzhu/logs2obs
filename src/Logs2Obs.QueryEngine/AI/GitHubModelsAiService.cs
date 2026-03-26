namespace Logs2Obs.QueryEngine.AI;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.AI;
using Logs2Obs.Core.Exceptions;
using Logs2Obs.Core.Graphs;
using Logs2Obs.Core.Models;
using Logs2Obs.QueryEngine.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

public sealed class GitHubModelsAiService(
    IHttpClientFactory httpClientFactory,
    IOptions<GitHubModelsOptions> opts,
    AiQueryAuditLogger auditLogger,
    ISqlSafetyValidator safetyValidator,
    ILogger<GitHubModelsAiService> logger) : IAiService
{
    private readonly GitHubModelsOptions _opts = opts.Value;

    public async Task<AiSqlResult> GenerateSqlAsync(
        string tenantId,
        string naturalLanguage,
        string schemaContext,
        CancellationToken ct = default)
    {
        _ = schemaContext;
        var ctx = new QueryContext { TenantId = tenantId };
        var result = await TranslateToSqlAsync(naturalLanguage, ctx, ct);
        return new AiSqlResult
        {
            Sql = result.Sql,
            Explanation = result.Explanation,
            SuggestedGraphType = result.SuggestedGraphType,
            InputTokenCount = 0,
            OutputTokenCount = 0,
            ModelUsed = _opts.Model
        };
    }

    public async Task<NlQueryResult> TranslateToSqlAsync(
        string naturalLanguage,
        QueryContext ctx,
        CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(ctx);
        var pipeline = new ResiliencePipelineBuilder<NlQueryResult>()
            .AddRetry(new RetryStrategyOptions<NlQueryResult>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = new PredicateBuilder<NlQueryResult>()
                    .Handle<HttpRequestException>()
            })
            .Build();

        return await pipeline.ExecuteAsync(async token =>
        {
            var http = httpClientFactory.CreateClient("github-models");
            http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl.TrimEnd('/')}/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
            request.Content = JsonContent.Create(new
            {
                model = _opts.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = naturalLanguage }
                },
                max_tokens = _opts.MaxTokens,
                temperature = 0.2
            });

            using var response = await http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: token);
            if (body is null || body.Choices is null || body.Choices.Count == 0)
                throw new AiQueryException("GitHub Models returned an empty response.");

            var content = body.Choices[0].Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                throw new AiQueryException("GitHub Models returned an empty response.");

            var parsed = ParseResult(content);
            var safetyReport = safetyValidator.Analyze(parsed.Sql);
            var audit = new AiQueryAudit
            {
                QueryId = Guid.NewGuid().ToString("n"),
                TenantId = ctx.TenantId,
                NaturalLanguageInput = naturalLanguage,
                SystemPrompt = systemPrompt,
                GeneratedSql = parsed.Sql,
                Explanation = parsed.Explanation,
                SuggestedGraphType = parsed.SuggestedGraphType.ToString(),
                SafetyReport = safetyReport,
                InputTokenCount = body.Usage?.PromptTokens ?? 0,
                OutputTokenCount = body.Usage?.CompletionTokens ?? 0,
                ModelUsed = _opts.Model
            };

            await auditLogger.LogAsync(audit, token);

            if (safetyReport.Errors.Count > 0)
                throw new SqlSafetyException(string.Join("; ", safetyReport.Errors));

            logger.LogInformation("GitHub Models generated SQL for tenant {TenantId}", ctx.TenantId);

            return parsed with { SafetyWarnings = safetyReport.Warnings };
        }, ct);
    }

    public Task<IReadOnlyList<GraphSuggestion>> SuggestGraphsAsync(
        QueryResultSchema schema,
        string? intent,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GraphSuggestion>>([]);

    private static string BuildSystemPrompt(QueryContext ctx)
    {
        var environments = ctx.Environments.Count > 0 ? string.Join(", ", ctx.Environments) : "none";
        var sources = ctx.KnownSources.Count > 0 ? string.Join(", ", ctx.KnownSources) : "none";
        var logTypes = ctx.LogTypes.Count > 0 ? string.Join(", ", ctx.LogTypes) : "none";

        return $"""
            You are a SQL assistant for logs2obs. Generate DuckDB/Athena compatible SQL.
            Table: logs
            Columns: tenant_id, timestamp, level, message, source_id, log_type, environment, category, trace_id, span_id,
                     request_id, service, status_code, duration_ms, attributes, year, month, day, hour
            Partitions: year, month, day, hour
            Rules:
            - Only SELECT statements.
            - Always include partition filters on year/month/day/hour.
            - Always include a LIMIT clause.
            - Prefer tenant_id = '{ctx.TenantId}'.
            - Output JSON with keys: sql, explanation, suggestedGraphType.
            - suggestedGraphType must be one of: {string.Join(", ", Enum.GetNames<GraphType>())}.
            Tenant context:
            - Environments: {environments}
            - KnownSources: {sources}
            - LogTypes: {logTypes}
            - HotRetentionDays: {ctx.HotRetentionDays}
            - WarmRetentionDays: {ctx.WarmRetentionDays}
            """;
    }

    private static NlQueryResult ParseResult(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var sql = root.TryGetProperty("sql", out var sqlElement) ? sqlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(sql))
                throw new AiQueryException("AI returned an unparseable response. Missing sql field.");

            var explanation = root.TryGetProperty("explanation", out var explanationElement)
                ? explanationElement.GetString() ?? string.Empty
                : string.Empty;

            var graphTypeRaw = root.TryGetProperty("suggestedGraphType", out var graphElement)
                ? graphElement.GetString()
                : null;

            var graphType = Enum.TryParse<GraphType>(graphTypeRaw, true, out var parsed)
                ? parsed
                : GraphType.LineChart;

            return new NlQueryResult
            {
                Sql = sql,
                Explanation = explanation,
                SuggestedGraphType = graphType
            };
        }
        catch (JsonException ex)
        {
            throw new AiQueryException($"AI returned an unparseable response: {ex.Message}");
        }
    }
}

file sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")] public List<Choice>? Choices { get; init; }
    [JsonPropertyName("usage")] public Usage? Usage { get; init; }
}

file sealed class Choice
{
    [JsonPropertyName("message")] public Message? Message { get; init; }
}

file sealed class Message
{
    [JsonPropertyName("content")] public string? Content { get; init; }
}

file sealed class Usage
{
    [JsonPropertyName("prompt_tokens")] public int? PromptTokens { get; init; }
    [JsonPropertyName("completion_tokens")] public int? CompletionTokens { get; init; }
}
