namespace Logs2Obs.QueryEngine.Tests.MatViews;

using System.Runtime.CompilerServices;
using Logs2Obs.Core.MatViews;
using Logs2Obs.QueryEngine.MatViews;
using Logs2Obs.QueryEngine.Options;
using Microsoft.Extensions.Options;

public class MatViewRefreshConsumerTests
{
    private const string TenantId = "tenant-1";
    private const string QueueName = "matview-refresh";

    [Fact]
    public async Task ExecuteAsync_OnMessage_CallsRefreshService()
    {
        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.SubmitAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed });

        var matViewEngine = new Mock<IMatViewEngine>();
        matViewEngine.Setup(x => x.RefreshAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var refreshService = new MatViewRefreshService(
            matViewEngine.Object,
            queryEngine.Object,
            NullLogger<MatViewRefreshService>.Instance);

        var bus = new SingleMessageBus(TenantId);
        var consumer = new MatViewRefreshConsumer(
            bus,
            refreshService,
            matViewEngine.Object,
            Options.Create(new QueryEngineOptions { MatViewRefreshQueue = QueueName }),
            NullLogger<MatViewRefreshConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await bus.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        matViewEngine.Verify(x => x.RefreshAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(StandardMatViews.All.Count));
    }

    [Fact]
    public async Task ExecuteAsync_WhenRefreshFails_DoesNotCrash()
    {
        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.SubmitAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var matViewEngine = new Mock<IMatViewEngine>();
        var refreshService = new MatViewRefreshService(
            matViewEngine.Object,
            queryEngine.Object,
            NullLogger<MatViewRefreshService>.Instance);

        var bus = new SingleMessageBus(TenantId);
        var consumer = new MatViewRefreshConsumer(
            bus,
            refreshService,
            matViewEngine.Object,
            Options.Create(new QueryEngineOptions { MatViewRefreshQueue = QueueName }),
            NullLogger<MatViewRefreshConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await bus.DeadLettered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        bus.Acknowledged.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_StopsOnCancellation()
    {
        var bus = new CancellationBus();
        var refreshService = new MatViewRefreshService(
            new Mock<IMatViewEngine>().Object,
            new Mock<IQueryEngine>().Object,
            NullLogger<MatViewRefreshService>.Instance);

        var consumer = new MatViewRefreshConsumer(
            bus,
            refreshService,
            new Mock<IMatViewEngine>().Object,
            Options.Create(new QueryEngineOptions { MatViewRefreshQueue = QueueName }),
            NullLogger<MatViewRefreshConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await bus.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        bus.SubscribeCalls.Should().Be(1);
        bus.AcknowledgeCount.Should().Be(0);
        bus.DeadLetterCount.Should().Be(0);
    }

    private sealed class SingleMessageBus(string tenantId) : IMessageBus
    {
        private readonly string _tenantId = tenantId;

        public TaskCompletionSource<bool> Acknowledged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> DeadLettered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PublishAsync<T>(string topic, T message, CancellationToken ct = default) => Task.CompletedTask;

        public Task AcknowledgeAsync(string receiptHandle, CancellationToken ct = default)
        {
            Acknowledged.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task DeadLetterAsync(string receiptHandle, string reason, CancellationToken ct = default)
        {
            DeadLettered.TrySetResult(true);
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(string queue, CancellationToken ct = default)
            => SingleMessageAsync<T>(_tenantId, ct);

        private static async IAsyncEnumerable<MessageEnvelope<T>> SingleMessageAsync<T>(
            string tenantId,
            [EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            yield return new MessageEnvelope<T>
            {
                Payload = CreatePayload<T>(tenantId),
                ReceiptHandle = "receipt-1",
                EnqueuedAt = DateTimeOffset.UtcNow
            };
            await Task.CompletedTask;
        }

        private static T CreatePayload<T>(string tenantId)
        {
            var type = typeof(T);
            var payload = Activator.CreateInstance(type, nonPublic: true);
            if (payload is null)
            {
                throw new InvalidOperationException($"Unable to create payload of type {type.Name}.");
            }

            var tenantProperty = type.GetProperty("TenantId");
            tenantProperty?.SetValue(payload, tenantId);
            return (T)payload;
        }
    }

    private sealed class CancellationBus : IMessageBus
    {
        public TaskCompletionSource<bool> Subscribed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SubscribeCalls { get; private set; }
        public int AcknowledgeCount { get; private set; }
        public int DeadLetterCount { get; private set; }

        public Task PublishAsync<T>(string topic, T message, CancellationToken ct = default) => Task.CompletedTask;

        public Task AcknowledgeAsync(string receiptHandle, CancellationToken ct = default)
        {
            AcknowledgeCount++;
            return Task.CompletedTask;
        }

        public Task DeadLetterAsync(string receiptHandle, string reason, CancellationToken ct = default)
        {
            DeadLetterCount++;
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<MessageEnvelope<T>> SubscribeAsync<T>(string queue, CancellationToken ct = default)
        {
            SubscribeCalls++;
            Subscribed.TrySetResult(true);
            return WaitForCancellationAsync<T>(ct);
        }

        private static async IAsyncEnumerable<MessageEnvelope<T>> WaitForCancellationAsync<T>(
            [EnumeratorCancellation] CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(25, CancellationToken.None);
            }

            yield break;
        }
    }
}
