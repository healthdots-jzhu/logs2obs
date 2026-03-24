namespace Logs2Obs.Core.Abstractions;

public sealed record MessageEnvelope<T>
{
    public required T Payload { get; init; }
    public required string ReceiptHandle { get; init; }
    public required DateTimeOffset EnqueuedAt { get; init; }
}
