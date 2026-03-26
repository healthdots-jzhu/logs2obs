namespace Logs2Obs.Adapters.Aws.MessageBus;

using Logs2Obs.Core.Abstractions;

public sealed class AwsMessageBus(
    AwsSnsMessageBus publisher,
    AwsSqsSubscriber subscriber) : IMessageBus
{
    public Task PublishAsync<T>(string topic, T message, CancellationToken ct = default)
        => publisher.PublishAsync(topic, message, ct);

    public IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(string queue, CancellationToken ct = default)
        => subscriber.SubscribeAsync<T>(queue, ct);

    public Task AcknowledgeAsync(string receiptHandle, CancellationToken ct = default)
        => subscriber.AcknowledgeAsync(receiptHandle, ct);

    public Task DeadLetterAsync(string receiptHandle, string reason, CancellationToken ct = default)
        => subscriber.DeadLetterAsync(receiptHandle, reason, ct);
}
