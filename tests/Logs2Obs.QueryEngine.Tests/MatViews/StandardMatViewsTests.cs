namespace Logs2Obs.QueryEngine.Tests.MatViews;

using Logs2Obs.Core.MatViews;

public class StandardMatViewsTests
{
    private static readonly string[] ExpectedNames =
    [
        "error_rate_per_minute",
        "latency_p99_per_service",
        "log_volume_by_type"
    ];

    [Fact]
    public void All_ContainsThreeViews()
    {
        var views = StandardMatViews.All;

        views.Should().HaveCount(3);
        views.Select(v => v.Name).Should().BeEquivalentTo(ExpectedNames);
    }

    [Fact]
    public void All_ErrorRatePerMinute_HasOneMinuteInterval()
    {
        var view = StandardMatViews.All.Single(v => v.Name == "error_rate_per_minute");

        view.RefreshIntervalSeconds.Should().Be(60);
    }

    [Fact]
    public void All_LatencyP99_HasFiveMinuteInterval()
    {
        var view = StandardMatViews.All.Single(v => v.Name == "latency_p99_per_service");

        view.RefreshIntervalSeconds.Should().Be(300);
    }

    [Fact]
    public void All_AllViewsHaveValidSql()
    {
        StandardMatViews.All.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v.Sql));
    }
}
