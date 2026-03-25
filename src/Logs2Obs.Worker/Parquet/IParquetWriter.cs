namespace Logs2Obs.Worker.Parquet;

using Logs2Obs.Core.Models;

public interface IParquetWriter
{
    Task<Stream> WriteAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct);
}
