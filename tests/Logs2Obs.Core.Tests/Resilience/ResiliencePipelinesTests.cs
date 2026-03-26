using Logs2Obs.Core.Resilience;

namespace Logs2Obs.Core.Tests.Resilience;

public class ResiliencePipelinesTests
{
    [Fact]
    public void ForExternalIo_ReturnsNonNullPipeline()
    {
        var pipeline = ResiliencePipelines.ForExternalIo<int>();

        pipeline.Should().NotBeNull();
    }

    [Fact]
    public async Task ForExternalIo_PipelineExecutesCallable()
    {
        var pipeline = ResiliencePipelines.ForExternalIo<int>();

        var result = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(5), CancellationToken.None);

        result.Should().Be(5);
    }

    [Fact]
    public async Task ForExternalIo_PipelineRetriesOnTransientException()
    {
        var pipeline = ResiliencePipelines.ForExternalIo<int>();
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("transient");
            }

            return ValueTask.FromResult(42);
        }, CancellationToken.None);

        result.Should().Be(42);
        attempts.Should().Be(2);
    }
}
