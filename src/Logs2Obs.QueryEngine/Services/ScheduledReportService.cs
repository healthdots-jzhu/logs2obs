namespace Logs2Obs.QueryEngine.Services;

using System.Runtime.CompilerServices;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Models;
using Microsoft.Extensions.Logging;

public sealed class ScheduledReportService(
    IMetadataStore metadataStore,
    IMessageBus messageBus,
    ILogger<ScheduledReportService> logger)
{
    private const string TableName = "scheduled_reports";

    private readonly IMetadataStore _metadataStore = metadataStore;
    private readonly IMessageBus _messageBus = messageBus;
    private readonly ILogger<ScheduledReportService> _logger = logger;

    public async Task ScheduleReportAsync(ScheduledReport report, CancellationToken ct)
    {
        var record = new ScheduledReportRecord
        {
            Key = BuildKey(report.TenantId, report.ReportId),
            ReportId = report.ReportId,
            TenantId = report.TenantId,
            Name = report.Name,
            SavedQueryId = report.SavedQueryId,
            CronSchedule = report.CronSchedule,
            Recipients = report.Recipients,
            IsEnabled = report.IsEnabled,
            LastRunAt = report.LastRunAt,
            CreatedAt = report.CreatedAt
        };

        await _metadataStore.PutAsync(TableName, record, ct);
        _logger.LogInformation("Scheduled report {ReportId} for tenant {TenantId}", report.ReportId, report.TenantId);
    }

    public async Task<ScheduledReport?> GetReportAsync(string reportId, string tenantId, CancellationToken ct)
    {
        var key = BuildKey(tenantId, reportId);
        var record = await _metadataStore.GetAsync<ScheduledReportRecord>(TableName, key, ct);
        return record is null ? null : Map(record);
    }

    public async IAsyncEnumerable<ScheduledReport> ListReportsAsync(string tenantId, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var record in _metadataStore.QueryAsync<ScheduledReportRecord>(TableName, r => r.TenantId == tenantId, ct))
            yield return Map(record);
    }

    public Task DeleteReportAsync(string reportId, string tenantId, CancellationToken ct)
    {
        var key = BuildKey(tenantId, reportId);
        _logger.LogInformation("Deleting scheduled report {ReportId} for tenant {TenantId}", reportId, tenantId);
        return _metadataStore.DeleteAsync(TableName, key, ct);
    }

    private static string BuildKey(string tenantId, string reportId) =>
        $"scheduled-report:{tenantId}:{reportId}";

    private static ScheduledReport Map(ScheduledReportRecord record) => new()
    {
        ReportId = record.ReportId,
        TenantId = record.TenantId,
        Name = record.Name,
        SavedQueryId = record.SavedQueryId,
        CronSchedule = record.CronSchedule,
        Recipients = record.Recipients,
        IsEnabled = record.IsEnabled,
        LastRunAt = record.LastRunAt,
        CreatedAt = record.CreatedAt
    };

    private sealed record ScheduledReportRecord
    {
        public required string Key { get; init; }
        public required string ReportId { get; init; }
        public required string TenantId { get; init; }
        public required string Name { get; init; }
        public required string SavedQueryId { get; init; }
        public required string CronSchedule { get; init; }
        public required string[] Recipients { get; init; }
        public bool IsEnabled { get; init; }
        public DateTimeOffset? LastRunAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
