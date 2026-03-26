namespace Logs2Obs.Adapters.Aws.Tests.QueryEngine;

using Logs2Obs.Adapters.Aws.QueryEngine;

public sealed class AthenaQueryEngineTests
{
    private readonly Type _sutType = typeof(AthenaQueryEngine);

    [Fact(Skip = "Requires AWS Athena execution state machine.")]
    public void SubmitAsync_WhenQueryValid_ReturnsExecutionId()
    {
        _ = _sutType;
    }

    [Fact(Skip = "Requires AWS Athena execution state machine.")]
    public void GetResultAsync_WhenCompleted_ReturnsRows()
    {
        _ = _sutType;
    }

    [Fact(Skip = "Requires AWS Athena cost estimation via API.")]
    public void EstimateCostAsync_WhenQueryProvided_ReturnsEstimate()
    {
        _ = _sutType;
    }
}
