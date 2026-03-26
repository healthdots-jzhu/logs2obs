namespace Logs2Obs.Adapters.Aws.Tests.MessageBus;

using Logs2Obs.Adapters.Aws.MessageBus;

public sealed class AwsSqsSubscriberTests
{
    private readonly Type _sutType = typeof(AwsSqsSubscriber);

    [Fact(Skip = "Requires AWS SQS queue interaction.")]
    public void SubscribeAsync_WhenQueueHasMessages_YieldsEnvelope()
    {
        _ = _sutType;
    }

    [Fact(Skip = "Requires AWS SQS queue interaction.")]
    public void AcknowledgeAsync_WhenCalled_RemovesMessage()
    {
        _ = _sutType;
    }

    [Fact(Skip = "Requires AWS SQS queue interaction.")]
    public void DeadLetterAsync_WhenCalled_SendsToDlq()
    {
        _ = _sutType;
    }
}
