namespace Logs2Obs.Adapters.Aws.Idempotency;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Resilience;
using StackExchange.Redis;

public sealed class ElastiCacheIdempotencyStore(
    IConnectionMultiplexer connection) : IIdempotencyStore
{
    private const string Prefix = "logs2obs:idem:";
    private readonly IDatabase _db = connection.GetDatabase();

    public async ValueTask<bool> CheckAndSetAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        var redisKey = (RedisKey)($"{Prefix}{key}");
        var result = await pipeline.ExecuteAsync(
            async _ => await _db.StringSetAsync(redisKey, "1", ttl, When.NotExists).ConfigureAwait(false), ct)
            .ConfigureAwait(false);

        return result;
    }

    public async ValueTask ExpireAsync(string key, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        var redisKey = (RedisKey)($"{Prefix}{key}");
        await pipeline.ExecuteAsync(
            async _ => await _db.KeyDeleteAsync(redisKey).ConfigureAwait(false), ct)
            .ConfigureAwait(false);

    }
}
