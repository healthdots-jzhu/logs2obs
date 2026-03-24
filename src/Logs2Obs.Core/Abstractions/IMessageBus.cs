namespace Logs2Obs.Core.Abstractions;

/// <summary>Cloud-agnostic message bus abstraction for pub/sub messaging.</summary>
public interface IMessageBus
{
    /// <summary>Publishes a message to the specified topic.</summary>
    Task PublishAsync<T>(string topic, T message, CancellationToken ct = default);

    /// <summary>Subscribes to a queue and streams incoming messages.</summary>
    IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(string queue, CancellationToken ct = default);

    /// <summary>Acknowledges successful processing of a message.</summary>
    Task AcknowledgeAsync(string receiptHandle, CancellationToken ct = default);

    /// <summary>Sends a message to the dead-letter queue with a reason.</summary>
    Task DeadLetterAsync(string receiptHandle, string reason, CancellationToken ct = default);
}
