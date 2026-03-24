using FluentAssertions;
using Logs2Obs.Adapters.Local.MessageBus;

namespace Logs2Obs.Adapters.Local.Tests.MessageBus;

public class InProcessChannelMessageBusTests
{
    private readonly InProcessChannelMessageBus _sut = new();

    [Fact]
    public async Task PublishAsync_ThenSubscribeAsync_DeliversMessage()
    {
        // Arrange
        const string topic = "test-topic";
        const string payload = "hello-world";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var receiveTask = Task.Run(async () =>
        {
            await foreach (var envelope in _sut.SubscribeAsync<string>(topic, cts.Token))
                return envelope.Payload;
            return null;
        }, cts.Token);

        await Task.Delay(50, cts.Token); // let subscriber register
        await _sut.PublishAsync(topic, payload, cts.Token);

        var received = await receiveTask;

        // Assert
        received.Should().Be(payload);
    }

    [Fact]
    public async Task PublishAsync_MultipleMessages_DeliveredInOrder()
    {
        // Arrange
        const string topic = "ordered-topic";
        var messages = new[] { "first", "second", "third" };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = new List<string>();

        // Act
        var receiveTask = Task.Run(async () =>
        {
            await foreach (var envelope in _sut.SubscribeAsync<string>(topic, cts.Token))
            {
                received.Add(envelope.Payload);
                if (received.Count == messages.Length)
                    break;
            }
        }, cts.Token);

        await Task.Delay(50, cts.Token);
        foreach (var msg in messages)
            await _sut.PublishAsync(topic, msg, cts.Token);

        await receiveTask;

        // Assert
        received.Should().ContainInOrder(messages);
    }

    [Fact]
    public async Task AcknowledgeAsync_DoesNotThrow()
    {
        // Arrange
        var receiptHandle = Guid.NewGuid().ToString();

        // Act
        var act = async () => await _sut.AcknowledgeAsync(receiptHandle);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeadLetterAsync_DoesNotThrow()
    {
        // Arrange
        var receiptHandle = Guid.NewGuid().ToString();

        // Act
        var act = async () => await _sut.DeadLetterAsync(receiptHandle, "test reason");

        // Assert
        await act.Should().NotThrowAsync();
    }
}
