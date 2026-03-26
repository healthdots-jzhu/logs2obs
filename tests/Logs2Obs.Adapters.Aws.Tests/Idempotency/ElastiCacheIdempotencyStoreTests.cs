namespace Logs2Obs.Adapters.Aws.Tests.Idempotency;

using Logs2Obs.Adapters.Aws.Idempotency;

public sealed class ElastiCacheIdempotencyStoreTests
{
    private readonly Type _sutType = typeof(ElastiCacheIdempotencyStore);

    [Fact(Skip = "Requires ElastiCache Redis connection.")]
    public void CheckAndSetAsync_WhenKeyNew_ReturnsTrue()
    {
        _ = _sutType;
    }

    [Fact(Skip = "Requires ElastiCache Redis connection.")]
    public void ExpireAsync_WhenKeyExists_RemovesEntry()
    {
        _ = _sutType;
    }
}
