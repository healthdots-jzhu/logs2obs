namespace Logs2Obs.QueryEngine.Tests.Routing;

using Logs2Obs.QueryEngine.Tests.Helpers;

public class QueryTierRouterTests
{
    [Fact]
    public void Route_WhenQueryHasFullTextSearch_ReturnsHot()
    {
        var router = new QueryTierRouter(NullLogger<QueryTierRouter>.Instance);
        var tenant = TestDataBuilders.AValidTenantSettings(7, 90);
        var now = DateTimeOffset.UtcNow;
        var query = TestDataBuilders.AValidParsedQuery(earliest: now.AddDays(-2), latest: now.AddDays(-1))
            with { HasFullTextSearch = true };

        var decision = router.Route(query, tenant);

        decision.Tier.Should().Be(QueryTier.Hot);
    }

    [Fact]
    public void Route_WhenNoTimeRange_ReturnsHot()
    {
        var router = new QueryTierRouter(NullLogger<QueryTierRouter>.Instance);
        var tenant = TestDataBuilders.AValidTenantSettings(7, 90);
        var query = TestDataBuilders.AValidParsedQuery(hasTimeFilter: false, hasLimit: true);

        var decision = router.Route(query, tenant);

        decision.Tier.Should().Be(QueryTier.Hot);
    }

    [Fact]
    public void Route_WhenEntirelyWithinHotWindow_ReturnsHot()
    {
        var router = new QueryTierRouter(NullLogger<QueryTierRouter>.Instance);
        var tenant = TestDataBuilders.AValidTenantSettings(7, 90);
        var now = DateTimeOffset.UtcNow;
        var query = TestDataBuilders.AValidParsedQuery(
            earliest: now.AddDays(-2),
            latest: now.AddDays(-1));

        var decision = router.Route(query, tenant);

        decision.Tier.Should().Be(QueryTier.Hot);
    }

    [Fact]
    public void Route_WhenEntirelyInWarmWindow_ReturnsWarm()
    {
        var router = new QueryTierRouter(NullLogger<QueryTierRouter>.Instance);
        var tenant = TestDataBuilders.AValidTenantSettings(7, 90);
        var now = DateTimeOffset.UtcNow;
        var query = TestDataBuilders.AValidParsedQuery(
            earliest: now.AddDays(-30),
            latest: now.AddDays(-8));

        var decision = router.Route(query, tenant);

        decision.Tier.Should().Be(QueryTier.Warm);
    }

    [Fact]
    public void Route_WhenEntirelyInColdStorage_ReturnsCold()
    {
        var router = new QueryTierRouter(NullLogger<QueryTierRouter>.Instance);
        var tenant = TestDataBuilders.AValidTenantSettings(7, 90);
        var now = DateTimeOffset.UtcNow;
        var query = TestDataBuilders.AValidParsedQuery(
            earliest: now.AddDays(-120),
            latest: now.AddDays(-100));

        var decision = router.Route(query, tenant);

        decision.Tier.Should().Be(QueryTier.Cold);
    }

    [Fact]
    public void Route_WhenSpansHotAndWarm_ReturnsCrossTier()
    {
        var router = new QueryTierRouter(NullLogger<QueryTierRouter>.Instance);
        var tenant = TestDataBuilders.AValidTenantSettings(7, 90);
        var now = DateTimeOffset.UtcNow;
        var query = TestDataBuilders.AValidParsedQuery(
            earliest: now.AddDays(-30),
            latest: now.AddDays(-2));

        var decision = router.Route(query, tenant);

        decision.Tier.Should().Be(QueryTier.CrossTier);
        decision.SubQueries.Should().NotBeNull();
        decision.SubQueries.Should().HaveCount(2);
    }
}
