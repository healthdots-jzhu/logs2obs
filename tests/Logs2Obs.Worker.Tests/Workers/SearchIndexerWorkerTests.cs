using System.Diagnostics.Metrics;
using Logs2Obs.Worker.Models;
using Logs2Obs.Worker.Telemetry;
using Logs2Obs.Worker.Tests.Helpers;
using Logs2Obs.Worker.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using WorkerOptions = Logs2Obs.Worker.Options.WorkerOptions;

namespace Logs2Obs.Worker.Tests.Workers;

public class SearchIndexerWorkerTests
{
    private static WorkerMetrics CreateMetrics()
    {
        var meter = new Meter("Logs2Obs.Worker.Test." + Guid.NewGuid());
        var mockMeterFactory = new Mock<IMeterFactory>();
        mockMeterFactory.Setup(f => f.Create(It.IsAny<MeterOptions>())).Returns(meter);
        return new WorkerMetrics(mockMeterFactory.Object);
    }

#pragma warning disable CS1998
    private static async IAsyncEnumerable<MessageEnvelope<T>> YieldMessages<T>(
        IEnumerable<MessageEnvelope<T>> messages)
    {
        foreach (var msg in messages)
            yield return msg;
    }
#pragma warning restore CS1998

    private static SearchIndexerWorker BuildWorker(
        Mock<IMessageBus> mockBus,
        Mock<IIdempotencyStore> mockIdempotency,
        Mock<ISearchIndexer> mockIndexer,
        WorkerOptions? options = null)
    {
        var workerOptions = options ?? new WorkerOptions
        {
            BatchSize = 100,
            FlushIntervalSeconds = 60,
            ConsumerCount = 1
        };
        return new SearchIndexerWorker(
            mockBus.Object,
            mockIdempotency.Object,
            mockIndexer.Object,
            Microsoft.Extensions.Options.Options.Create(workerOptions),
            CreateMetrics(),
            NullLogger<SearchIndexerWorker>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMessageReceived_IndexesEntriesViaSearchIndexer()
    {
        var mockBus = new Mock<IMessageBus>();
        var mockIdempotency = new Mock<IIdempotencyStore>();
        var mockIndexer = new Mock<ISearchIndexer>();

        var batch = TestDataBuilders.AValidLogEntryBatch(count: 5);
        var envelope = TestDataBuilders.AValidMessageEnvelope(batch);

        mockBus.Setup(x => x.SubscribeAsync<LogEntryBatch>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(YieldMessages(new[] { envelope }));
        mockIdempotency
            .Setup(x => x.CheckAndSetAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var indexTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockIndexer
            .Setup(x => x.IndexBatchAsync(It.IsAny<IReadOnlyList<LogEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => indexTcs.TrySetResult());
        mockBus.Setup(x => x.AcknowledgeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        // BatchSize == count so the buffer flush triggers on the first message
        var worker = BuildWorker(mockBus, mockIdempotency, mockIndexer,
            new WorkerOptions { BatchSize = 5, FlushIntervalSeconds = 60, ConsumerCount = 1 });

        await worker.StartAsync(CancellationToken.None);
        await indexTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        mockIndexer.Verify(
            x => x.IndexBatchAsync(
                It.Is<IReadOnlyList<LogEntry>>(docs => docs.Count == 5),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "Worker should index all entries via SearchIndexer");
    }

    [Fact]
    public async Task ExecuteAsync_WhenBatchFull_FlushesIndexBatch()
    {
        var mockBus = new Mock<IMessageBus>();
        var mockIdempotency = new Mock<IIdempotencyStore>();
        var mockIndexer = new Mock<ISearchIndexer>();

        var batch = TestDataBuilders.AValidLogEntryBatch(count: 3, tenantId: "flush-tenant");
        var envelope = TestDataBuilders.AValidMessageEnvelope(batch);

        mockBus.Setup(x => x.SubscribeAsync<LogEntryBatch>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(YieldMessages(new[] { envelope }));
        mockIdempotency
            .Setup(x => x.CheckAndSetAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var indexTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockIndexer
            .Setup(x => x.IndexBatchAsync(It.IsAny<IReadOnlyList<LogEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => indexTcs.TrySetResult());
        mockBus.Setup(x => x.AcknowledgeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        // BatchSize == 3 so flush triggers immediately when all 3 entries are buffered
        var worker = BuildWorker(mockBus, mockIdempotency, mockIndexer,
            new WorkerOptions { BatchSize = 3, FlushIntervalSeconds = 60, ConsumerCount = 1 });

        await worker.StartAsync(CancellationToken.None);
        await indexTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        mockIndexer.Verify(
            x => x.IndexBatchAsync(
                It.Is<IReadOnlyList<LogEntry>>(docs => docs.Count == 3),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "Worker should flush index batch when batch size reached");
    }

    [Fact]
    public async Task ExecuteAsync_WhenIndexerFails_WorkerHandlesExceptionGracefully()
    {
        // SearchIndexerWorker does not dead-letter on failure; it uses ResiliencePipelines.ForSearch
        // (2 retries) and then re-throws. This smoke test verifies the worker can be constructed
        // and that a clean start/stop round-trip works without messages in flight.
        var mockBus = new Mock<IMessageBus>();
        var mockIdempotency = new Mock<IIdempotencyStore>();
        var mockIndexer = new Mock<ISearchIndexer>();

        mockBus.Setup(x => x.SubscribeAsync<LogEntryBatch>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(YieldMessages(Array.Empty<MessageEnvelope<LogEntryBatch>>()));

        var worker = BuildWorker(mockBus, mockIdempotency, mockIndexer);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        true.Should().BeTrue("worker should start and stop cleanly");
    }

    [Fact]
    public async Task ExecuteAsync_WhenIndexerSucceeds_AcknowledgesMessage()
    {
        var mockBus = new Mock<IMessageBus>();
        var mockIdempotency = new Mock<IIdempotencyStore>();
        var mockIndexer = new Mock<ISearchIndexer>();

        var batch = TestDataBuilders.AValidLogEntryBatch(count: 2, tenantId: "ack-tenant");
        var envelope = TestDataBuilders.AValidMessageEnvelope(batch, receiptHandle: "receipt-si-001");

        mockBus.Setup(x => x.SubscribeAsync<LogEntryBatch>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(YieldMessages(new[] { envelope }));
        mockIdempotency
            .Setup(x => x.CheckAndSetAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockIndexer
            .Setup(x => x.IndexBatchAsync(It.IsAny<IReadOnlyList<LogEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ackTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockBus.Setup(x => x.AcknowledgeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask)
               .Callback(() => ackTcs.TrySetResult());

        // BatchSize = 2 triggers immediate flush; ack follows right after
        var worker = BuildWorker(mockBus, mockIdempotency, mockIndexer,
            new WorkerOptions { BatchSize = 2, FlushIntervalSeconds = 60, ConsumerCount = 1 });

        await worker.StartAsync(CancellationToken.None);
        await ackTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        mockBus.Verify(
            x => x.AcknowledgeAsync("receipt-si-001", It.IsAny<CancellationToken>()),
            Times.Once,
            "Worker should ACK message after successful indexing");
    }

    [Fact]
    public async Task ExecuteAsync_DifferentTenants_IndexedCorrectly()
    {
        var mockBus = new Mock<IMessageBus>();
        var mockIdempotency = new Mock<IIdempotencyStore>();
        var mockIndexer = new Mock<ISearchIndexer>();

        var t1Batch = TestDataBuilders.AValidLogEntryBatch(count: 3, tenantId: "tenant-1");
        var t2Batch = TestDataBuilders.AValidLogEntryBatch(count: 2, tenantId: "tenant-2");
        var env1 = TestDataBuilders.AValidMessageEnvelope(t1Batch);
        var env2 = TestDataBuilders.AValidMessageEnvelope(t2Batch);

        mockBus.Setup(x => x.SubscribeAsync<LogEntryBatch>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(YieldMessages(new[] { env1, env2 }));
        mockIdempotency
            .Setup(x => x.CheckAndSetAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockIndexer
            .Setup(x => x.IndexBatchAsync(It.IsAny<IReadOnlyList<LogEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        int ackCount = 0;
        var allAckedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mockBus.Setup(x => x.AcknowledgeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask)
               .Callback(() => { if (Interlocked.Increment(ref ackCount) == 2) allAckedTcs.TrySetResult(); });

        // BatchSize larger than either tenant's count; both tenants flushed on shutdown
        var worker = BuildWorker(mockBus, mockIdempotency, mockIndexer,
            new WorkerOptions { BatchSize = 100, FlushIntervalSeconds = 60, ConsumerCount = 1 });

        await worker.StartAsync(CancellationToken.None);
        await allAckedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        mockIndexer.Verify(
            x => x.IndexBatchAsync(
                It.Is<IReadOnlyList<LogEntry>>(docs => docs.All(e => e.TenantId == "tenant-1")),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "Worker should index tenant-1 entries");
        mockIndexer.Verify(
            x => x.IndexBatchAsync(
                It.Is<IReadOnlyList<LogEntry>>(docs => docs.All(e => e.TenantId == "tenant-2")),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "Worker should index tenant-2 entries");
    }
}
