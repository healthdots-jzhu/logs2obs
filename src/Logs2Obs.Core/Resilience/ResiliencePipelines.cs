namespace Logs2Obs.Core.Resilience;

using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

/// <summary>Pre-built Polly resilience pipelines for external I/O calls.</summary>
public static class ResiliencePipelines
{
    /// <summary>
    /// Resilience pipeline for general external I/O: 3 exponential retries with jitter + 50% circuit breaker.
    /// </summary>
    public static ResiliencePipeline<T> ForExternalIo<T>() =>
        new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = 3,
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                Delay            = TimeSpan.FromMilliseconds(200)
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<T>
            {
                FailureRatio      = 0.5,
                SamplingDuration  = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration     = TimeSpan.FromSeconds(30)
            })
            .Build();

    /// <summary>
    /// Resilience pipeline for search operations: 2 retries at 500ms + 10-second timeout.
    /// </summary>
    public static ResiliencePipeline<T> ForSearch<T>() =>
        new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = 2,
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                Delay            = TimeSpan.FromMilliseconds(500)
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(10)
            })
            .Build();

    /// <summary>
    /// Resilience pipeline for storage operations: 3 exponential retries + 60-second timeout.
    /// </summary>
    public static ResiliencePipeline<T> ForStorage<T>() =>
        new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = 3,
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                Delay            = TimeSpan.FromMilliseconds(200)
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(60)
            })
            .Build();
}
