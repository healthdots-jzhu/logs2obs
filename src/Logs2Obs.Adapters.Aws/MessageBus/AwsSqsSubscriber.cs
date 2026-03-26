namespace Logs2Obs.Adapters.Aws.MessageBus;

using System.Collections.Concurrent;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Logs2Obs.Adapters.Aws.Options;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Resilience;
using Microsoft.Extensions.Options;

public sealed class AwsSqsSubscriber(
    IAmazonSQS sqs,
    IOptions<AwsAdaptersOptions> options) : IMessageBus
{
    private readonly SqsOptions _opts = options.Value.Sqs;
    private readonly ConcurrentDictionary<string, ReceiptContext> _receiptContexts = new();

    public Task PublishAsync<T>(string topic, T message, CancellationToken ct = default)
        => throw new NotSupportedException("Use AwsSnsMessageBus for publishing");

    public async IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(
        string queue,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var queueUrl = ResolveQueueUrl(queue);
        var pipeline = ResiliencePipelines.ForExternalIo<ReceiveMessageResponse>();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var request = new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = _opts.MaxMessages,
                WaitTimeSeconds = _opts.WaitTimeSeconds,
                MessageSystemAttributeNames = new List<string> { "SentTimestamp" }
            };

            var response = await pipeline.ExecuteAsync(
                async token => await sqs.ReceiveMessageAsync(request, token).ConfigureAwait(false), ct)
                .ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                MessageEnvelope<T>? envelope = null;
                try
                {
                    var payload = JsonSerializer.Deserialize<T>(message.Body);
                    if (payload is null)
                        continue;

                    var enqueuedAt = DateTimeOffset.UtcNow;
                    if (message.Attributes.TryGetValue("SentTimestamp", out var sent)
                        && long.TryParse(sent, out var sentMs))
                    {
                        enqueuedAt = DateTimeOffset.FromUnixTimeMilliseconds(sentMs);
                    }

                    _receiptContexts[message.ReceiptHandle] = new ReceiptContext(queue, message.Body);
                    envelope = new MessageEnvelope<T>
                    {
                        Payload = payload,
                        ReceiptHandle = message.ReceiptHandle,
                        EnqueuedAt = enqueuedAt
                    };
                }
                catch (JsonException)
                {
                    continue;
                }

                if (envelope is not null)
                    yield return envelope;
            }
        }
    }

    public async Task AcknowledgeAsync(string receiptHandle, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            if (!_receiptContexts.TryRemove(receiptHandle, out var ctx))
                return true;

            var request = new DeleteMessageRequest
            {
                QueueUrl = ResolveQueueUrl(ctx.QueueName),
                ReceiptHandle = receiptHandle
            };
            await sqs.DeleteMessageAsync(request, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    public async Task DeadLetterAsync(string receiptHandle, string reason, CancellationToken ct = default)
    {
        if (!_receiptContexts.TryRemove(receiptHandle, out var ctx))
            return;

        var dlqUrl = ResolveDlqUrl(ctx.QueueName);
        if (string.IsNullOrWhiteSpace(dlqUrl))
            return;

        var sendPipeline = ResiliencePipelines.ForExternalIo<bool>();
        await sendPipeline.ExecuteAsync(async token =>
        {
            var sendRequest = new SendMessageRequest
            {
                QueueUrl = dlqUrl,
                MessageBody = ctx.Body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["dlqReason"] = new()
                    {
                        DataType = "String",
                        StringValue = reason
                    }
                }
            };
            await sqs.SendMessageAsync(sendRequest, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

        var deletePipeline = ResiliencePipelines.ForExternalIo<bool>();
        await deletePipeline.ExecuteAsync(async token =>
        {
            var deleteRequest = new DeleteMessageRequest
            {
                QueueUrl = ResolveQueueUrl(ctx.QueueName),
                ReceiptHandle = receiptHandle
            };
            await sqs.DeleteMessageAsync(deleteRequest, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    private string ResolveQueueUrl(string queue)
    {
        if (_opts.QueueUrlMap.TryGetValue(queue, out var url))
            return url;

        return queue;
    }

    private string? ResolveDlqUrl(string queue)
    {
        if (_opts.DlqUrlMap.TryGetValue(queue, out var url))
            return url;

        return null;
    }

    private sealed record ReceiptContext(string QueueName, string Body);
}
