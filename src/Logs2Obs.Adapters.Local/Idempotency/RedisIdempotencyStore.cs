namespace Logs2Obs.Adapters.Local.Idempotency;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Adapters.Local.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

public sealed class RedisIdempotencyStore(
    IOptions<RedisOptions> options,
    ILogger<RedisIdempotencyStore>? logger = null) : IIdempotencyStore
{
    private readonly IDatabase _db = ConnectionMultiplexer
        .Connect(options.Value.ConnectionString).GetDatabase();
    private readonly string _prefix = $"{options.Value.InstanceName}idem:";
    private readonly ILogger<RedisIdempotencyStore> _logger =
        logger ?? NullLogger<RedisIdempotencyStore>.Instance;

    public async ValueTask<bool> CheckAndSetAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var redisKey = (RedisKey)($"{_prefix}{key}");
        var isNew = await _db.StringSetAsync(redisKey, "1", ttl, When.NotExists);
        if (!isNew)
            _logger.LogDebug("Idempotency: duplicate key {Key}", key);
        return isNew;
    }

    public async ValueTask ExpireAsync(string key, CancellationToken ct = default)
    {
        var redisKey = (RedisKey)($"{_prefix}{key}");
        await _db.KeyDeleteAsync(redisKey);
        _logger.LogDebug("Idempotency: expired key {Key}", key);
    }
}
