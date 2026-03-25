namespace Logs2Obs.QueryEngine.Tests.Services;

using Logs2Obs.QueryEngine.Services;
using Logs2Obs.QueryEngine.Tests.Helpers;

public class ScheduledReportServiceTests
{
    [Fact]
    public async Task ScheduleReportAsync_PersistsToMetadataStore()
    {
        var store = new InMemoryMetadataStore();
        var messageBus = new Mock<IMessageBus>();
        var service = new ScheduledReportService(store, messageBus.Object, NullLogger<ScheduledReportService>.Instance);
        var report = BuildReport("report-1", "tenant-1");

        await service.ScheduleReportAsync(report, CancellationToken.None);

        var key = $"scheduled-report:{report.TenantId}:{report.ReportId}";
        store.TryGet("scheduled_reports", key, out var record).Should().BeTrue();
        InMemoryMetadataStore.GetPropertyValue<string>(record!, "Key").Should().Be(key);
        InMemoryMetadataStore.GetPropertyValue<string>(record!, "Name").Should().Be(report.Name);
    }

    [Fact]
    public async Task GetReportAsync_WhenExists_ReturnsReport()
    {
        var store = new InMemoryMetadataStore();
        var messageBus = new Mock<IMessageBus>();
        var service = new ScheduledReportService(store, messageBus.Object, NullLogger<ScheduledReportService>.Instance);
        var report = BuildReport("report-2", "tenant-1");

        await service.ScheduleReportAsync(report, CancellationToken.None);

        var result = await service.GetReportAsync(report.ReportId, report.TenantId, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(report);
    }

    [Fact]
    public async Task ListReportsAsync_ReturnsTenantReports()
    {
        var store = new InMemoryMetadataStore();
        var messageBus = new Mock<IMessageBus>();
        var service = new ScheduledReportService(store, messageBus.Object, NullLogger<ScheduledReportService>.Instance);
        var first = BuildReport("report-3", "tenant-1");
        var second = BuildReport("report-4", "tenant-1");
        var other = BuildReport("report-5", "tenant-2");

        await service.ScheduleReportAsync(first, CancellationToken.None);
        await service.ScheduleReportAsync(second, CancellationToken.None);
        await service.ScheduleReportAsync(other, CancellationToken.None);

        var results = new List<ScheduledReport>();
        await foreach (var item in service.ListReportsAsync("tenant-1", CancellationToken.None))
        {
            results.Add(item);
        }

        results.Should().HaveCount(2);
        results.Select(r => r.ReportId).Should().BeEquivalentTo(new[] { "report-3", "report-4" });
    }

    [Fact]
    public async Task DeleteReportAsync_RemovesFromMetadataStore()
    {
        var store = new InMemoryMetadataStore();
        var messageBus = new Mock<IMessageBus>();
        var service = new ScheduledReportService(store, messageBus.Object, NullLogger<ScheduledReportService>.Instance);
        var report = BuildReport("report-6", "tenant-1");

        await service.ScheduleReportAsync(report, CancellationToken.None);

        await service.DeleteReportAsync(report.ReportId, report.TenantId, CancellationToken.None);

        var key = $"scheduled-report:{report.TenantId}:{report.ReportId}";
        store.TryGet("scheduled_reports", key, out _).Should().BeFalse();
    }

    private static ScheduledReport BuildReport(string reportId, string tenantId) => new()
    {
        ReportId = reportId,
        TenantId = tenantId,
        Name = "Daily errors",
        SavedQueryId = "saved-query-1",
        CronSchedule = "0 0 * * *",
        Recipients = ["qa@example.com"],
        IsEnabled = true,
        LastRunAt = DateTimeOffset.UtcNow.AddDays(-1),
        CreatedAt = DateTimeOffset.UtcNow
    };
}
