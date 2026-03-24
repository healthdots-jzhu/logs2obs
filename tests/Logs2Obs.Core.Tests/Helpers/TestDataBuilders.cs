using Logs2Obs.Core.Models;

namespace Logs2Obs.Core.Tests.Helpers;

public static class TestDataBuilders
{
    public static LogEntryDto AValidLogEntryDto() => new()
    {
        SourceId = "payment-service",
        LogType = "Application",
        Level = "Information",
        Environment = "production",
        Category = "api",
        Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
        Message = "Request processed successfully"
    };

    public static TenantSettings AValidTenantSettings(int hotDays = 3, int warmDays = 90) => new()
    {
        TenantId = "t-test",
        Name = "Test Tenant",
        HotRetentionDays = hotDays,
        WarmRetentionDays = warmDays,
        MaxQueryScanGb = 10,
        RequireTimeFilter = false,
        RequireLimit = false,
        IsActive = true
    };

    public static MetricDto AValidMetricDto() => new()
    {
        MetricName = "http-latency",
        Unit = "ms",
        Value = 123.4,
        MetricType = "Histogram"
    };
}
