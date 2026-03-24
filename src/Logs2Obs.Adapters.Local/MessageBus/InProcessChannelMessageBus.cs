namespace Logs2Obs.Adapters.Local.MessageBus;

using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Logs2Obs.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class InProcessChannelMessageBus(
    ILogger<InProcessChannelMessageBus>? logger = null,
    int boundedCapacity = 10_000) : IMessageBus
{
    private readonly ILogger<InProcessChannelMessageBus> _logger =
        logger ?? NullLogger<InProcessChannelMessageBus>.Instance;
    private readonly ConcurrentDictionary<string, Channel<object>> _channels = new();

    private Channel<object> GetOrCreateChannel(string topic) =>
        _channels.GetOrAdd(topic, _ => Channel.CreateBounded<object>(new BoundedChannelOptions(boundedCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false
        }));

    public async Task PublishAsync<T>(string topic, T message, CancellationToken ct = default)
    {
        var ch = GetOrCreateChannel(topic);
        await ch.Writer.WriteAsync(message!, ct);
        _logger.LogDebug("In-process: published to {Topic}", topic);
    }

    public async IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(string queue, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var ch = GetOrCreateChannel(queue);
        await foreach (var item in ch.Reader.ReadAllAsync(ct))
        {
            var payload = item is T typed ? typed : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(item))!;
            yield return new MessageEnvelope<T>
            {
                Payload = payload,
                ReceiptHandle = Guid.NewGuid().ToString("N"),
                EnqueuedAt = DateTimeOffset.UtcNow
            };
        }
    }

    public Task AcknowledgeAsync(string receiptHandle, CancellationToken ct = default)
    {
        _logger.LogDebug("In-process: ack {Handle} (no-op)", receiptHandle);
        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(string receiptHandle, string reason, CancellationToken ct = default)
    {
        _logger.LogWarning("In-process: DLQ {Handle} — {Reason} (message dropped)", receiptHandle, reason);
        return Task.CompletedTask;
    }
}
