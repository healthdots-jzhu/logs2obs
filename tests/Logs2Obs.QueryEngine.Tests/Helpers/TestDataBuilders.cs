namespace Logs2Obs.QueryEngine.Tests.Helpers;

public static class TestDataBuilders
{
    public static TenantSettings AValidTenantSettings(int hotDays = 7, int warmDays = 90)
        => new()
        {
            TenantId = "tenant-1",
            Name = "Test Tenant",
            HotRetentionDays = hotDays,
            WarmRetentionDays = warmDays,
            MaxQueryScanGb = 10.0,
            RequireTimeFilter = true,
            RequireLimit = true,
            IsActive = true
        };

    public static ParsedQuery AValidParsedQuery(
        bool hasTimeFilter = true,
        bool hasLimit = true,
        DateTimeOffset? earliest = null,
        DateTimeOffset? latest = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ParsedQuery
        {
            QueryId = Guid.NewGuid().ToString("N"),
            HasFullTextSearch = false,
            HasTimeFilter = hasTimeFilter,
            HasLimit = hasLimit,
            EarliestTimestamp = hasTimeFilter ? earliest ?? now.AddDays(-1) : null,
            LatestTimestamp = hasTimeFilter ? latest ?? now : null
        };
    }

    public static ExecuteSqlQuery AValidExecuteSqlQuery(string sql = "SELECT * FROM logs LIMIT 100")
        => new()
        {
            TenantId = "tenant-1",
            Sql = sql
        };
}
