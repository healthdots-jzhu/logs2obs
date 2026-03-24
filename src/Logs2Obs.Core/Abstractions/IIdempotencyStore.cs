namespace Logs2Obs.Core.Abstractions;

/// <summary>Store for exactly-once processing guarantees using TTL-backed deduplication.</summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically checks if the key exists and sets it if not.
    /// Returns true if the key was new (not a duplicate); false if it already existed.
    /// </summary>
    ValueTask<bool> CheckAndSetAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Immediately expires the given key from the idempotency store.</summary>
    ValueTask ExpireAsync(string key, CancellationToken ct = default);
}
