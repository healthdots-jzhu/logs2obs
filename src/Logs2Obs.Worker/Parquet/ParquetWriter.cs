namespace Logs2Obs.Worker.Parquet;

using global::Parquet;
using global::Parquet.Data;
using global::Parquet.Schema;
using global::Parquet.Serialization;
using Logs2Obs.Core.Models;
using System.Text.Json;

public sealed class ParquetWriter : IParquetWriter
{
    public async Task<Stream> WriteAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct)
    {
        var stream = new MemoryStream();
        var records = entries.Select(MapToParquetRecord).ToList();

        await ParquetSerializer.SerializeAsync(records, stream, cancellationToken: ct);

        stream.Position = 0;
        return stream;
    }

    private static LogEntryParquetRecord MapToParquetRecord(LogEntry entry) => new()
    {
        Id = entry.Id,
        SourceId = entry.SourceId,
        LogType = entry.LogType.ToString(),
        Level = entry.Level.ToString(),
        Environment = entry.Environment,
        Category = entry.Category ?? string.Empty,
        TimestampUnixMs = entry.TimestampUnixMs,
        Message = entry.Message,
        TraceId = entry.TraceId ?? string.Empty,
        TenantId = entry.TenantId,
        SchemaVersion = (int)entry.SchemaVersion,
        Tags = entry.Tags is null ? string.Empty : JsonSerializer.Serialize(entry.Tags)
    };

    private sealed class LogEntryParquetRecord
    {
        public required string Id { get; init; }
        public required string SourceId { get; init; }
        public required string LogType { get; init; }
        public required string Level { get; init; }
        public required string Environment { get; init; }
        public required string Category { get; init; }
        public required long TimestampUnixMs { get; init; }
        public required string Message { get; init; }
        public required string TraceId { get; init; }
        public required string TenantId { get; init; }
        public required int SchemaVersion { get; init; }
        public required string Tags { get; init; }
    }
}
