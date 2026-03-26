namespace Logs2Obs.QueryEngine.Tests.Alerts;

using System.Net.Http;
using System.Runtime.CompilerServices;
using Logs2Obs.QueryEngine.Alerts;
using Logs2Obs.QueryEngine.Models;
using Logs2Obs.QueryEngine.Options;
using Microsoft.Extensions.Options;

public class AlertEvaluationConsumerTests
{
    private const string TenantId = "tenant-1";
    private const string AlertQueue = "alerts";
    private const string EventsQueue = "events";

    [Fact]
    public async Task EvaluateAsync_WhenThresholdBreached_PublishesAlertFiredEvent()
    {
        var rule = BuildRule("rule-1", "SELECT 1", ">", 5, "slack");
        var batch = BuildBatch();
        var metadataStore = BuildMetadataStore(rule);

        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.SubmitAsync(TenantId, rule.Sql, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed, ResultLocation = "[10]" });

        var publishTcs = new TaskCompletionSource<AlertFiredEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var messageBus = new Mock<IMessageBus>();
        messageBus.Setup(x => x.SubscribeAsync<LogEntryBatch>(AlertQueue, It.IsAny<CancellationToken>()))
            .Returns(SingleMessage(batch));
        messageBus.Setup(x => x.PublishAsync(EventsQueue, It.IsAny<AlertFiredEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, AlertFiredEvent, CancellationToken>((_, evt, _) => publishTcs.TrySetResult(evt))
            .Returns(Task.CompletedTask);
        messageBus.Setup(x => x.AcknowledgeAsync("receipt-1", It.IsAny<CancellationToken>()))
            .Callback(() => ackTcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var consumer = new AlertEvaluationConsumer(
            messageBus.Object,
            metadataStore.Object,
            queryEngine.Object,
            new AlertEvaluationMetrics(),
            CreateOptions(),
            NullLogger<AlertEvaluationConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        var evt = await publishTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await ackTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        evt.RuleId.Should().Be(rule.RuleId);
        evt.ActualValue.Should().Be(10);
        messageBus.Verify(x => x.PublishAsync(EventsQueue, It.IsAny<AlertFiredEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_WhenThresholdNotBreached_DoesNotPublish()
    {
        var rule = BuildRule("rule-2", "SELECT 2", ">", 10, null);
        var batch = BuildBatch();
        var metadataStore = BuildMetadataStore(rule);

        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.SubmitAsync(TenantId, rule.Sql, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed, ResultLocation = "[3]" });

        var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageBus = new Mock<IMessageBus>();
        messageBus.Setup(x => x.SubscribeAsync<LogEntryBatch>(AlertQueue, It.IsAny<CancellationToken>()))
            .Returns(SingleMessage(batch));
        messageBus.Setup(x => x.AcknowledgeAsync("receipt-1", It.IsAny<CancellationToken>()))
            .Callback(() => ackTcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var consumer = new AlertEvaluationConsumer(
            messageBus.Object,
            metadataStore.Object,
            queryEngine.Object,
            new AlertEvaluationMetrics(),
            CreateOptions(),
            NullLogger<AlertEvaluationConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await ackTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        messageBus.Verify(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<AlertFiredEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_WhenNoRulesForTenant_DoesNothing()
    {
        var batch = BuildBatch();
        var metadataStore = new Mock<IMetadataStore>();
        metadataStore.Setup(x => x.GetAsync<IReadOnlyList<AlertRule>>("alert-rules", $"alert-rules:{TenantId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AlertRule>());

        var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageBus = new Mock<IMessageBus>();
        messageBus.Setup(x => x.SubscribeAsync<LogEntryBatch>(AlertQueue, It.IsAny<CancellationToken>()))
            .Returns(SingleMessage(batch));
        messageBus.Setup(x => x.AcknowledgeAsync("receipt-1", It.IsAny<CancellationToken>()))
            .Callback(() => ackTcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var consumer = new AlertEvaluationConsumer(
            messageBus.Object,
            metadataStore.Object,
            new Mock<IQueryEngine>().Object,
            new AlertEvaluationMetrics(),
            CreateOptions(),
            NullLogger<AlertEvaluationConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await ackTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        messageBus.Verify(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<AlertFiredEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        metadataStore.Verify(x => x.QueryAsync<AlertRule>(It.IsAny<string>(), It.IsAny<Func<AlertRule, bool>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_WhenQueryEngineFails_RetriesWithPolly()
    {
        var rule = BuildRule("rule-3", "SELECT 3", ">", 100, null);
        var batch = BuildBatch();
        var metadataStore = BuildMetadataStore(rule);

        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.SetupSequence(x => x.SubmitAsync(TenantId, rule.Sql, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("transient"))
            .ThrowsAsync(new HttpRequestException("transient"))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed, ResultLocation = "[1]" });

        var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageBus = new Mock<IMessageBus>();
        messageBus.Setup(x => x.SubscribeAsync<LogEntryBatch>(AlertQueue, It.IsAny<CancellationToken>()))
            .Returns(SingleMessage(batch));
        messageBus.Setup(x => x.AcknowledgeAsync("receipt-1", It.IsAny<CancellationToken>()))
            .Callback(() => ackTcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var consumer = new AlertEvaluationConsumer(
            messageBus.Object,
            metadataStore.Object,
            queryEngine.Object,
            new AlertEvaluationMetrics(),
            CreateOptions(),
            NullLogger<AlertEvaluationConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await ackTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        queryEngine.Verify(x => x.SubmitAsync(TenantId, rule.Sql, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task EvaluateAsync_EvaluatesRulesInParallel()
    {
        var rules = new List<AlertRule>
        {
            BuildRule("rule-a", "SELECT 10", ">", 100, null),
            BuildRule("rule-b", "SELECT 20", ">", 100, null),
            BuildRule("rule-c", "SELECT 30", ">", 100, null)
        };

        var batch = BuildBatch();
        var metadataStore = new Mock<IMetadataStore>();
        metadataStore.Setup(x => x.GetAsync<IReadOnlyList<AlertRule>>("alert-rules", $"alert-rules:{TenantId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);

        var queryEngine = new Mock<IQueryEngine>();
        var startedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateLock = new object();
        var inFlight = 0;
        var maxInFlight = 0;
        var startedCount = 0;

        queryEngine.Setup(x => x.SubmitAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>(async (_, __, ct) =>
            {
                lock (gateLock)
                {
                    inFlight++;
                    if (inFlight > maxInFlight)
                    {
                        maxInFlight = inFlight;
                    }

                    startedCount++;
                    if (startedCount == rules.Count)
                    {
                        startedTcs.TrySetResult(true);
                    }
                }

                await releaseTcs.Task.WaitAsync(ct);

                lock (gateLock)
                {
                    inFlight--;
                }

                return new QuerySubmitResult { Status = QueryStatus.Completed, ResultLocation = "[0]" };
            });

        var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageBus = new Mock<IMessageBus>();
        messageBus.Setup(x => x.SubscribeAsync<LogEntryBatch>(AlertQueue, It.IsAny<CancellationToken>()))
            .Returns(SingleMessage(batch));
        messageBus.Setup(x => x.AcknowledgeAsync("receipt-1", It.IsAny<CancellationToken>()))
            .Callback(() => ackTcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var consumer = new AlertEvaluationConsumer(
            messageBus.Object,
            metadataStore.Object,
            queryEngine.Object,
            new AlertEvaluationMetrics(),
            CreateOptions(),
            NullLogger<AlertEvaluationConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        maxInFlight.Should().BeGreaterThan(1);
        releaseTcs.TrySetResult(true);
        await ackTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.StopAsync(CancellationToken.None);

        queryEngine.Verify(x => x.SubmitAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(rules.Count));
    }

    private static IOptions<QueryEngineOptions> CreateOptions() =>
        Options.Create(new QueryEngineOptions
        {
            AlertEvaluatorQueue = AlertQueue,
            SystemEventsQueue = EventsQueue
        });

    private static LogEntryBatch BuildBatch() =>
        new(Array.Empty<LogEntry>(), TenantId, "batch-1");

    private static AlertRule BuildRule(string ruleId, string sql, string op, double threshold, string? channel) =>
        new()
        {
            RuleId = ruleId,
            TenantId = TenantId,
            Name = $"Rule {ruleId}",
            Sql = sql,
            ThresholdOperator = op,
            ThresholdValue = threshold,
            NotificationChannel = channel,
            IsEnabled = true
        };

    private static Mock<IMetadataStore> BuildMetadataStore(AlertRule rule)
    {
        var metadataStore = new Mock<IMetadataStore>();
        metadataStore.Setup(x => x.GetAsync<IReadOnlyList<AlertRule>>("alert-rules", $"alert-rules:{TenantId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AlertRule> { rule });
        return metadataStore;
    }

    private static IAsyncEnumerable<MessageEnvelope<LogEntryBatch>> SingleMessage(LogEntryBatch batch)
        => SingleMessageAsync(batch);

    private static async IAsyncEnumerable<MessageEnvelope<LogEntryBatch>> SingleMessageAsync(
        LogEntryBatch batch,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        yield return new MessageEnvelope<LogEntryBatch>
        {
            Payload = batch,
            ReceiptHandle = "receipt-1",
            EnqueuedAt = DateTimeOffset.UtcNow
        };
        await Task.CompletedTask;
    }
}
