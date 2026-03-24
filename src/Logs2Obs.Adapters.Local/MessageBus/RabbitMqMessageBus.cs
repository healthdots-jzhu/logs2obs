namespace Logs2Obs.Adapters.Local.MessageBus;

using System.Text.Json;
using System.Threading.Channels;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Resilience;
using Logs2Obs.Adapters.Local.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

public sealed class RabbitMqMessageBus(
    IConnection connection,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqMessageBus> logger) : IMessageBus, IAsyncDisposable
{
    private const string ExchangeName = "logs2obs.exchange";
    private readonly RabbitMqOptions _opts = options.Value;

    public async Task PublishAsync<T>(string topic, T message, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async ctx =>
        {
            await using var channel = await connection.CreateChannelAsync(cancellationToken: ctx);
            await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: ctx);

            var body = JsonSerializer.SerializeToUtf8Bytes(message);
            var props = new BasicProperties { Persistent = true };
            await channel.BasicPublishAsync(ExchangeName, topic, true, props, body, ctx);
            return true;
        }, ct);
        logger.LogDebug("Published message to topic {Topic}", topic);
    }

    public async IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(string queue, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(queue, ExchangeName, queue, cancellationToken: ct);
        await channel.BasicQosAsync(0, _opts.PrefetchCount, false, cancellationToken: ct);

        var internalChannel = Channel.CreateBounded<MessageEnvelope<T>>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true
        });

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var payload = JsonSerializer.Deserialize<T>(ea.Body.Span)!;
                var envelope = new MessageEnvelope<T>
                {
                    Payload = payload,
                    ReceiptHandle = ea.DeliveryTag.ToString(),
                    EnqueuedAt = DateTimeOffset.UtcNow
                };
                await internalChannel.Writer.WriteAsync(envelope, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deserializing message from queue {Queue}", queue);
            }
        };

        await channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer, cancellationToken: ct);

        await foreach (var envelope in internalChannel.Reader.ReadAllAsync(ct))
        {
            yield return envelope;
        }
    }

    public async Task AcknowledgeAsync(string receiptHandle, CancellationToken ct = default)
    {
        if (!ulong.TryParse(receiptHandle, out var deliveryTag))
        {
            logger.LogWarning("Invalid receipt handle for ack: {Handle}", receiptHandle);
            return;
        }
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: ct);
    }

    public async Task DeadLetterAsync(string receiptHandle, string reason, CancellationToken ct = default)
    {
        if (!ulong.TryParse(receiptHandle, out var deliveryTag))
        {
            logger.LogWarning("Invalid receipt handle for DLQ: {Handle}", receiptHandle);
            return;
        }
        logger.LogWarning("Dead-lettering message {Handle}: {Reason}", receiptHandle, reason);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken: ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
