namespace Logs2Obs.Adapters.Aws.Tests.MessageBus;

using Logs2Obs.Adapters.Aws.MessageBus;

public sealed class AwsSnsMessageBusTests
{
    private readonly Type _sutType = typeof(AwsSnsMessageBus);

    [Fact(Skip = "Requires AWS SNS topic routing and credentials.")]
    public void PublishAsync_WhenTopicExists_PublishesMessage()
    {
        _ = _sutType;
    }
}
