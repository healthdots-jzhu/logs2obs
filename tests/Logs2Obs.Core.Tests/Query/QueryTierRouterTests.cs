using FluentAssertions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Query;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logs2Obs.Core.Tests.Query;

public class QueryTierRouterTests
{
    private readonly QueryTierRouter _sut = new(NullLogger<QueryTierRouter>.Instance);

    private static TenantSettings MakeTenant(int hotDays = 3, int warmDays = 90) => new()
    {
        TenantId = "t-test",
        Name = "Test Tenant",
        HotRetentionDays = hotDays,
        WarmRetentionDays = warmDays
    };

    [Fact]
    public void Route_WhenQueryHasFullTextSearch_ReturnsHotTier()
    {
        var query = new ParsedQuery
        {
            QueryId = "q1",
            HasFullTextSearch = true,
            EarliestTimestamp = DateTimeOffset.UtcNow.AddDays(-10),
            LatestTimestamp = DateTimeOffset.UtcNow,
            HasTimeFilter = true,
            HasLimit = true
        };

        var result = _sut.Route(query, MakeTenant());

        result.Tier.Should().Be(QueryTier.Hot);
    }

    [Fact]
    public void Route_WhenTimestampWithinHotWindow_ReturnsHotTier()
    {
        // Both timestamps within the last 3 days → Hot
        var query = new ParsedQuery
        {
            QueryId = "q2",
            HasFullTextSearch = false,
            EarliestTimestamp = DateTimeOffset.UtcNow.AddDays(-2),
            LatestTimestamp = DateTimeOffset.UtcNow.AddHours(-1),
            HasTimeFilter = true,
            HasLimit = true
        };

        var result = _sut.Route(query, MakeTenant());

        result.Tier.Should().Be(QueryTier.Hot);
    }

    [Fact]
    public void Route_WhenTimestampInWarmWindow_ReturnsWarmTier()
    {
        // Both timestamps between 3 and 90 days ago → Warm
        var query = new ParsedQuery
        {
            QueryId = "q3",
            HasFullTextSearch = false,
            EarliestTimestamp = DateTimeOffset.UtcNow.AddDays(-60),
            LatestTimestamp = DateTimeOffset.UtcNow.AddDays(-5),
            HasTimeFilter = true,
            HasLimit = true
        };

        var result = _sut.Route(query, MakeTenant());

        result.Tier.Should().Be(QueryTier.Warm);
    }

    [Fact]
    public void Route_WhenTimestampInColdStorage_ReturnsColdTierWithWarning()
    {
        // Both timestamps older than 90 days → Cold with Warning
        var query = new ParsedQuery
        {
            QueryId = "q4",
            HasFullTextSearch = false,
            EarliestTimestamp = DateTimeOffset.UtcNow.AddDays(-200),
            LatestTimestamp = DateTimeOffset.UtcNow.AddDays(-100),
            HasTimeFilter = true,
            HasLimit = true
        };

        var result = _sut.Route(query, MakeTenant());

        result.Tier.Should().Be(QueryTier.Cold);
        result.Warning.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Route_WhenTimestampSpansWarmAndCold_ReturnsCrossTierWithTwoSubQueries()
    {
        // Earliest in cold (>90 days), Latest in warm (between 3–90 days) → CrossTier
        var query = new ParsedQuery
        {
            QueryId = "q5",
            HasFullTextSearch = false,
            EarliestTimestamp = DateTimeOffset.UtcNow.AddDays(-120),
            LatestTimestamp = DateTimeOffset.UtcNow.AddDays(-30),
            HasTimeFilter = true,
            HasLimit = true
        };

        var result = _sut.Route(query, MakeTenant());

        result.Tier.Should().Be(QueryTier.CrossTier);
        result.SubQueries.Should().HaveCount(2);
        result.SubQueries.Should().Contain(sq => sq.Tier == QueryTier.Warm);
        result.SubQueries.Should().Contain(sq => sq.Tier == QueryTier.Cold);
    }

    [Fact]
    public void Route_WhenTimestampSpansHotAndWarm_ReturnsCrossTierWithTwoSubQueries()
    {
        // Earliest in warm (>3 days ago), Latest in hot (<3 days ago) → CrossTier
        var query = new ParsedQuery
        {
            QueryId = "q6",
            HasFullTextSearch = false,
            EarliestTimestamp = DateTimeOffset.UtcNow.AddDays(-30),
            LatestTimestamp = DateTimeOffset.UtcNow.AddDays(-1),
            HasTimeFilter = true,
            HasLimit = true
        };

        var result = _sut.Route(query, MakeTenant());

        result.Tier.Should().Be(QueryTier.CrossTier);
        result.SubQueries.Should().HaveCount(2);
        result.SubQueries.Should().Contain(sq => sq.Tier == QueryTier.Hot);
        result.SubQueries.Should().Contain(sq => sq.Tier == QueryTier.Warm);
    }

    [Fact]
    public void Route_WhenFullTextSearchWithOldTimestamp_StillReturnsHotTier()
    {
        // Rule 1 (full-text) trumps all timestamp rules
        var query = new ParsedQuery
        {
            QueryId = "q7",
            HasFullTextSearch = true,
            EarliestTimestamp = DateTimeOffset.UtcNow.AddDays(-200),
            LatestTimestamp = DateTimeOffset.UtcNow.AddDays(-100),
            HasTimeFilter = true,
            HasLimit = true
        };

        var result = _sut.Route(query, MakeTenant());

        result.Tier.Should().Be(QueryTier.Hot);
    }

    [Fact]
    public void Route_WhenEntirelyInColdTier_SubQueriesAreEmpty()
    {
        var query = new ParsedQuery
        {
            QueryId = "q8",
            HasFullTextSearch = false,
            EarliestTimestamp = DateTimeOffset.UtcNow.AddDays(-200),
            LatestTimestamp = DateTimeOffset.UtcNow.AddDays(-100),
            HasTimeFilter = true,
            HasLimit = true
        };

        var result = _sut.Route(query, MakeTenant());

        result.Tier.Should().Be(QueryTier.Cold);
        result.SubQueries.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Route_WithCustomRetentionSettings_RoutesAccordingToTenantConfig()
    {
        // Tenant with 1-day hot window and 30-day warm window
        var tenant = MakeTenant(hotDays: 1, warmDays: 30);

        var query = new ParsedQuery
        {
            QueryId = "q9",
            HasFullTextSearch = false,
            EarliestTimestamp = DateTimeOffset.UtcNow.AddDays(-3),
            LatestTimestamp = DateTimeOffset.UtcNow.AddDays(-2),
            HasTimeFilter = true,
            HasLimit = true
        };

        // With 1-day hot window, 2–3 days ago is in the warm range
        var result = _sut.Route(query, tenant);

        result.Tier.Should().Be(QueryTier.Warm);
    }
}
