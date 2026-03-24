namespace Logs2Obs.Core.Mapping;

using Logs2Obs.Core.Models;

/// <summary>Maps between DTOs and domain objects, enforcing system-generated fields.</summary>
public static class DtoMapper
{
    /// <summary>
    /// Maps a <see cref="LogEntryDto"/> to a <see cref="LogEntry"/>.
    /// CRITICAL: Id, TenantId, IngestedAt, and IngestionMode are always set by this method — never from the DTO.
    /// </summary>
    public static LogEntry ToDomain(LogEntryDto dto, string tenantId, IngestionMode mode) =>
        new()
        {
            Id                = Guid.CreateVersion7().ToString("N"),
            SourceId          = dto.SourceId,
            LogType           = Enum.Parse<LogType>(dto.LogType, ignoreCase: true),
            Level             = Enum.Parse<LogLevel>(dto.Level, ignoreCase: true),
            Environment       = dto.Environment,
            Category          = dto.Category,
            TimestampUnixMs   = dto.Timestamp.ToUnixTimeMilliseconds(),
            Message           = dto.Message,
            TraceId           = dto.TraceId,
            StackTrace        = dto.StackTrace,
            Tags              = dto.Tags,
            Metric            = dto.Metric is null ? null : MapMetric(dto.Metric),
            TenantId          = tenantId,
            IngestedAt        = DateTimeOffset.UtcNow,
            IngestionMode     = mode,
            SchemaVersion     = 1
        };

    /// <summary>Maps a <see cref="LogEntry"/> to a <see cref="LogEntryDto"/>.</summary>
    public static LogEntryDto ToDto(LogEntry entry) => new()
    {
        SourceId    = entry.SourceId,
        LogType     = entry.LogType.ToString(),
        Level       = entry.Level.ToString(),
        Environment = entry.Environment,
        Category    = entry.Category,
        Timestamp   = DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampUnixMs),
        Message     = entry.Message,
        TraceId     = entry.TraceId,
        StackTrace  = entry.StackTrace,
        Tags        = entry.Tags,
        Metric      = entry.Metric is null ? null : new MetricDto
        {
            MetricName = entry.Metric.MetricName,
            Unit       = entry.Metric.Unit,
            Value      = entry.Metric.Value,
            MetricType = entry.Metric.MetricType.ToString()
        }
    };

    private static MetricEntry MapMetric(MetricDto dto) => new()
    {
        MetricName = dto.MetricName,
        Unit       = dto.Unit,
        Value      = dto.Value,
        MetricType = Enum.Parse<MetricType>(dto.MetricType, ignoreCase: true)
    };
}
