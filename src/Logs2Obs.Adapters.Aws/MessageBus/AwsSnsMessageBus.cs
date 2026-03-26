namespace Logs2Obs.Adapters.Aws.MessageBus;

using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Logs2Obs.Adapters.Aws.Options;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Resilience;
using Microsoft.Extensions.Options;

public sealed class AwsSnsMessageBus(
    IAmazonSimpleNotificationService sns,
    IOptions<AwsAdaptersOptions> options) : IMessageBus
{
    private readonly SnsOptions _opts = options.Value.Sns;

    public async Task PublishAsync<T>(string topic, T message, CancellationToken ct = default)
    {
        var pipeline = ResiliencePipelines.ForExternalIo<bool>();
        await pipeline.ExecuteAsync(async token =>
        {
            var arn = ResolveTopicArn(topic);
            var payload = JsonSerializer.Serialize(message);
            var request = new PublishRequest
            {
                TopicArn = arn,
                Message = payload
            };
            await sns.PublishAsync(request, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

    }

    public IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(string queue, CancellationToken ct = default)
        => throw new NotSupportedException("Use AwsSqsSubscriber for subscribing");

    public Task AcknowledgeAsync(string receiptHandle, CancellationToken ct = default)
        => throw new NotSupportedException("Use AwsSqsSubscriber for subscribing");

    public Task DeadLetterAsync(string receiptHandle, string reason, CancellationToken ct = default)
        => throw new NotSupportedException("Use AwsSqsSubscriber for subscribing");

    private string ResolveTopicArn(string topic)
    {
        if (_opts.TopicArnMap.TryGetValue(topic, out var arn))
            return arn;

        return topic;
    }
}
