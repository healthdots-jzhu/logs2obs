using System.Threading.RateLimiting;

namespace Logs2Obs.Api.Tests.RateLimiting;

/// <summary>
/// Tests for tenant-based rate limiting using System.Threading.RateLimiting.
/// Tests token bucket and sliding window algorithms in isolation.
/// </summary>
public class TenantRateLimiterTests
{
    [Fact]
    public async Task TokenBucket_WhenUnderLimit_AllowsRequests()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = 10,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });

        // Act
        var results = new List<RateLimitLease>();
        for (int i = 0; i < 5; i++)
        {
            results.Add(await rateLimiter.AcquireAsync(permitCount: 1));
        }

        // Assert
        results.Should().HaveCount(5);
        results.Should().OnlyContain(lease => lease.IsAcquired);
        
        // Cleanup
        foreach (var lease in results)
        {
            lease.Dispose();
        }
    }

    [Fact]
    public async Task TokenBucket_WhenLimitExceeded_RejectsRequest()
    {
        // Arrange
        var rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 5,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10), // Long period to ensure no replenishment
            TokensPerPeriod = 5,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });

        // Act - consume all tokens
        var successfulLeases = new List<RateLimitLease>();
        for (int i = 0; i < 5; i++)
        {
            successfulLeases.Add(await rateLimiter.AcquireAsync(permitCount: 1));
        }

        // Try one more (should fail)
        var rejectedLease = await rateLimiter.AcquireAsync(permitCount: 1);

        // Assert
        successfulLeases.Should().OnlyContain(lease => lease.IsAcquired);
        rejectedLease.IsAcquired.Should().BeFalse();
        
        // Cleanup
        foreach (var lease in successfulLeases)
        {
            lease.Dispose();
        }
        rejectedLease.Dispose();
    }

    [Fact]
    public async Task TokenBucket_AfterReplenishment_AllowsRequests()
    {
        // Arrange
        var replenishmentPeriod = TimeSpan.FromMilliseconds(100);
        var rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 2,
            ReplenishmentPeriod = replenishmentPeriod,
            TokensPerPeriod = 2,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });

        // Act - consume all tokens
        var firstBatch = new[]
        {
            await rateLimiter.AcquireAsync(permitCount: 1),
            await rateLimiter.AcquireAsync(permitCount: 1)
        };

        // Wait for replenishment
        await Task.Delay(replenishmentPeriod + TimeSpan.FromMilliseconds(50));

        // Try again after replenishment
        var secondBatch = new[]
        {
            await rateLimiter.AcquireAsync(permitCount: 1),
            await rateLimiter.AcquireAsync(permitCount: 1)
        };

        // Assert
        firstBatch.Should().OnlyContain(lease => lease.IsAcquired);
        secondBatch.Should().OnlyContain(lease => lease.IsAcquired);
        
        // Cleanup
        foreach (var lease in firstBatch.Concat(secondBatch))
        {
            lease.Dispose();
        }
    }

    [Fact]
    public async Task SlidingWindow_WhenUnderLimit_AllowsRequests()
    {
        // Arrange
        var rateLimiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(1),
            SegmentsPerWindow = 2,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });

        // Act
        var results = new List<RateLimitLease>();
        for (int i = 0; i < 5; i++)
        {
            results.Add(await rateLimiter.AcquireAsync(permitCount: 1));
        }

        // Assert
        results.Should().HaveCount(5);
        results.Should().OnlyContain(lease => lease.IsAcquired);
        
        // Cleanup
        foreach (var lease in results)
        {
            lease.Dispose();
        }
    }

    [Fact]
    public async Task SlidingWindow_WhenLimitExceeded_RejectsRequests()
    {
        // Arrange
        var rateLimiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromSeconds(10), // Long window to ensure limit is hit
            SegmentsPerWindow = 2,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });

        // Act - consume all permits
        var successfulLeases = new List<RateLimitLease>();
        for (int i = 0; i < 3; i++)
        {
            successfulLeases.Add(await rateLimiter.AcquireAsync(permitCount: 1));
        }

        // Try one more (should fail)
        var rejectedLease = await rateLimiter.AcquireAsync(permitCount: 1);

        // Assert
        successfulLeases.Should().OnlyContain(lease => lease.IsAcquired);
        rejectedLease.IsAcquired.Should().BeFalse();
        
        // Cleanup
        foreach (var lease in successfulLeases)
        {
            lease.Dispose();
        }
        rejectedLease.Dispose();
    }

    [Fact]
    public async Task RateLimiter_DifferentTenants_HaveIsolatedBuckets()
    {
        // Arrange - two separate limiters for two tenants
        var tenant1Limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 3,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            TokensPerPeriod = 3,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });

        var tenant2Limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 3,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            TokensPerPeriod = 3,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });

        // Act - exhaust tenant1's tokens
        var tenant1Leases = new List<RateLimitLease>();
        for (int i = 0; i < 3; i++)
        {
            tenant1Leases.Add(await tenant1Limiter.AcquireAsync(permitCount: 1));
        }
        var tenant1Rejected = await tenant1Limiter.AcquireAsync(permitCount: 1);

        // tenant2 should still have all tokens available
        var tenant2Leases = new List<RateLimitLease>();
        for (int i = 0; i < 3; i++)
        {
            tenant2Leases.Add(await tenant2Limiter.AcquireAsync(permitCount: 1));
        }

        // Assert
        tenant1Leases.Should().OnlyContain(lease => lease.IsAcquired);
        tenant1Rejected.IsAcquired.Should().BeFalse();
        tenant2Leases.Should().OnlyContain(lease => lease.IsAcquired);
        
        // Cleanup
        foreach (var lease in tenant1Leases.Concat(tenant2Leases))
        {
            lease.Dispose();
        }
        tenant1Rejected.Dispose();
    }
}
