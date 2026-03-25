namespace Logs2Obs.Worker.Options;

public sealed class WorkerOptions
{
    public int ConsumerCount { get; init; } = 4;
    public int BatchSize { get; init; } = 1_000;
    public int FlushIntervalSeconds { get; init; } = 5;
    public int ChannelCapacity { get; init; } = 50_000;
    public int MaxParallelism { get; init; } = 8;
    public string StorageWriterQueue { get; init; } = "ls-storage-writer";
    public string SearchIndexerQueue { get; init; } = "ls-search-indexer";
    public int RetryCount { get; init; } = 3;
    public int RetryBaseDelayMs { get; init; } = 500;
}
