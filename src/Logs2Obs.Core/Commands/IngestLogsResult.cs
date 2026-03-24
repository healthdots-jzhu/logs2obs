namespace Logs2Obs.Core.Commands;

public sealed record IngestLogsResult(int Accepted, int Rejected, string BatchId);
