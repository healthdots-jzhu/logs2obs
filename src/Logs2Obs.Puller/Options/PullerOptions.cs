namespace Logs2Obs.Puller.Options;

public sealed class PullerOptions
{
    public int BatchSize { get; set; } = 500;
    public int MaxConcurrentJobs { get; set; } = 4;
    public string StorageWriterQueue { get; set; } = "ls-storage-writer";
}
