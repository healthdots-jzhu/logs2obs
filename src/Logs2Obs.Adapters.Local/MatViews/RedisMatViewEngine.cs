namespace Logs2Obs.Adapters.Local.MatViews;

using System.Text.Json;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Adapters.Local.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

public sealed class RedisMatViewEngine(
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> options,
    ILogger<RedisMatViewEngine> logger) : IMatViewEngine
{
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly string _instanceName = options.Value.InstanceName;

    private string ViewKey(string tenantId, string viewName) =>
        $"{_instanceName}matview:{tenantId}:{viewName}";

    private string StaleKey(string tenantId, string viewName) =>
        $"{_instanceName}matview:{tenantId}:{viewName}:needs-refresh";

    public async Task RefreshAsync(string tenantId, string viewName, CancellationToken ct = default)
    {
        await _db.StringSetAsync((RedisKey)StaleKey(tenantId, viewName), "1");
        logger.LogWarning("MatViewEngine: RefreshAsync is a no-op for local adapter. Stale flag set for {TenantId}/{ViewName}", tenantId, viewName);
    }

    public async Task<MatViewResult> QueryAsync(string tenantId, string viewName, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var key = (RedisKey)ViewKey(tenantId, viewName);
        var value = await _db.StringGetAsync(key);
        if (!value.HasValue)
        {
            return new MatViewResult
            {
                IsFresh = false,
                Data = [],
                Source = "local-redis-missing"
            };
        }

        try
        {
            var data = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(value.ToString());
            var isFresh = await IsFreshAsync(tenantId, viewName, ct);
            return new MatViewResult
            {
                IsFresh = isFresh,
                Data = data?.Select(d => (IReadOnlyDictionary<string, object>)d).ToList() ?? [],
                Source = "local-redis"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deserializing mat view {ViewName} for tenant {TenantId}", viewName, tenantId);
            return new MatViewResult { IsFresh = false, Data = [], Source = "local-redis-error" };
        }
    }

    public async Task<bool> IsFreshAsync(string tenantId, string viewName, CancellationToken ct = default)
    {
        var staleFlag = await _db.KeyExistsAsync((RedisKey)StaleKey(tenantId, viewName));
        return !staleFlag;
    }
}
