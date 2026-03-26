namespace Logs2Obs.QueryEngine.Tests.Replay;

using System.Runtime.CompilerServices;
using Logs2Obs.QueryEngine.Options;
using Logs2Obs.QueryEngine.Replay;
using Microsoft.Extensions.Options;

public class ReplayWorkerTests
{
    private const string TenantId = "tenant-1";
    private const string JobId = "job-1";
    private const string QueueName = "system-events";

    [Fact]
    public async Task ExecuteAsync_OnReplayStartedEvent_UpdatesStatusToRunning()
    {
        var (worker, bus, metadataStore, _) = BuildWorker();

        await worker.StartAsync(CancellationToken.None);
        await bus.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        metadataStore.UpdatedJobs.First().Status.Should().Be(ReplayStatus.Running);
    }

    [Fact]
    public async Task ExecuteAsync_ListsParquetFilesFromObjectStore()
    {
        var prefixes = new List<string>();
        var (worker, bus, metadataStore, objectStore) = BuildWorker(prefixes);

        await worker.StartAsync(CancellationToken.None);
        await bus.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var expectedPrefix = $"logs/{TenantId}/{new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero):yyyy/MM}/";
        prefixes.Should().ContainSingle()
            .Which.Should().Be(expectedPrefix);
        objectStore.ListCalls.Should().Be(1);
        metadataStore.UpdatedJobs.Should().Contain(job => job.Status == ReplayStatus.Running);
    }

    [Fact]
    public async Task ExecuteAsync_WhenComplete_UpdatesStatusToCompleted()
    {
        var (worker, bus, metadataStore, _) = BuildWorker();

        await worker.StartAsync(CancellationToken.None);
        await bus.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        metadataStore.UpdatedJobs.Last().Status.Should().Be(ReplayStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFileFetchFails_UpdatesStatusToFailed()
    {
        var (worker, bus, metadataStore, objectStore) = BuildWorker();
        objectStore.FailReads = true;
        objectStore.Files = new List<string> { "logs/tenant-1/2024/05/file1.parquet" };

        await worker.StartAsync(CancellationToken.None);
        await bus.DeadLettered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        metadataStore.UpdatedJobs.Should().Contain(job => job.Status == ReplayStatus.Failed);
        bus.Acknowledged.Task.IsCompleted.Should().BeFalse();
    }

    private static (ReplayWorker Worker, TestBus Bus, TestMetadataStore MetadataStore, TestObjectStore ObjectStore) BuildWorker(
        List<string>? prefixes = null)
    {
        var bus = new TestBus();
        var metadataStore = new TestMetadataStore();
        var objectStore = new TestObjectStore(prefixes);
        var options = Options.Create(new QueryEngineOptions
        {
            SystemEventsQueue = QueueName,
            ReplayObjectPrefix = "logs",
            IngestQueue = "ingest"
        });

        var worker = new ReplayWorker(
            bus,
            objectStore,
            metadataStore,
            options,
            NullLogger<ReplayWorker>.Instance);

        return (worker, bus, metadataStore, objectStore);
    }

    private sealed class TestBus : IMessageBus
    {
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
            => SingleMessageAsync<T>(ct);

        private static async IAsyncEnumerable<MessageEnvelope<T>> SingleMessageAsync<T>(
            [EnumeratorCancellation] CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var payload = (T)(object)new ReplayStartedEvent
            {
                JobId = JobId,
                TenantId = TenantId,
                From = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero),
                To = new DateTimeOffset(2024, 5, 15, 0, 0, 0, TimeSpan.Zero),
                Options = new ReplayOptions { ReindexSearch = false, MaxParallelFiles = 1 }
            };

            yield return new MessageEnvelope<T>
            {
                Payload = payload,
                ReceiptHandle = "receipt-1",
                EnqueuedAt = DateTimeOffset.UtcNow
            };
            await Task.CompletedTask;
        }
    }

    private sealed class TestObjectStore : IObjectStore
    {
        private readonly List<string> _prefixes;
        public bool FailReads { get; set; }
        public List<string> Files { get; set; } = new();
        public int ListCalls { get; private set; }

        public TestObjectStore(List<string>? prefixes)
        {
            _prefixes = prefixes ?? new List<string>();
        }

        public Task WriteAsync(string key, Stream content, string contentType, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<Stream?> ReadAsync(string key, CancellationToken ct = default)
        {
            if (FailReads)
            {
                throw new IOException("read failed");
            }

            return Task.FromResult<Stream?>(null);
        }

        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);

        public IAsyncEnumerable<string> ListAsync(string prefix, CancellationToken ct = default)
        {
            ListCalls++;
            _prefixes.Add(prefix);
            return ToAsyncEnumerable(Files, ct);
        }

        private static async IAsyncEnumerable<string> ToAsyncEnumerable(
            IEnumerable<string> items,
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }
    }

    private sealed class TestMetadataStore : IMetadataStore
    {
        public List<ReplayJob> UpdatedJobs { get; } = new();

        public Task<T?> GetAsync<T>(string table, string key, CancellationToken ct = default)
        {
            if (table != "replay-jobs" || key != JobId || typeof(T) != typeof(Logs2Obs.Core.Models.ReplayJob))
            {
                return Task.FromResult<T?>(default);
            }

            var job = new Logs2Obs.Core.Models.ReplayJob
            {
                JobId = JobId,
                TenantId = TenantId,
                From = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero),
                To = new DateTimeOffset(2024, 5, 15, 0, 0, 0, TimeSpan.Zero),
                Options = new ReplayOptions(),
                Status = ReplayStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };

            return Task.FromResult((T?)(object?)job);
        }

        public Task PutAsync<T>(string table, T entity, CancellationToken ct = default)
        {
            if (entity is Logs2Obs.Core.Models.ReplayJob job)
            {
                UpdatedJobs.Add(job);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(string table, string key, CancellationToken ct = default) => Task.CompletedTask;

        public IAsyncEnumerable<T> QueryAsync<T>(string table, Func<T, bool> filter, CancellationToken ct = default)
            => System.Linq.AsyncEnumerable.Empty<T>();
    }
}
