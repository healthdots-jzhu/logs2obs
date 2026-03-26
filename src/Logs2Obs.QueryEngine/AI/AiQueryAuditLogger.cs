namespace Logs2Obs.QueryEngine.AI;

using System.Text.Json.Serialization;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.AI;
using Microsoft.Extensions.Logging;

public sealed class AiQueryAuditLogger(
    IMetadataStore metadataStore,
    ILogger<AiQueryAuditLogger> logger)
{
    private readonly IMetadataStore _metadataStore = metadataStore;
    private readonly ILogger<AiQueryAuditLogger> _logger = logger;

    public async Task LogAsync(AiQueryAudit audit, CancellationToken ct = default)
    {
        var key = $"ai-audit:{audit.TenantId}:{audit.QueryId}";
        try
        {
            await _metadataStore.PutAsync("ai-audit", new AiQueryAuditEnvelope
            {
                Key = key,
                Audit = audit
            }, ct);
            _logger.LogInformation("Stored AI audit {AuditKey}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store AI audit {AuditKey}", key);
        }
    }
}

file sealed record AiQueryAuditEnvelope
{
    [JsonPropertyName("key")] public required string Key { get; init; }
    public required AiQueryAudit Audit { get; init; }
}
