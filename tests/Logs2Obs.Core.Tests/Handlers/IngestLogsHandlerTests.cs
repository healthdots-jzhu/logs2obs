using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Commands;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logs2Obs.Core.Tests.Handlers;

public class IngestLogsHandlerTests
{
    [Fact]
    public async Task Handle_WithValidEntries_PublishesBatchToStorageAndSearchQueues()
    {
        var bus = new Mock<IMessageBus>();
        IReadOnlyList<LogEntry>? storageBatch = null;
        IReadOnlyList<LogEntry>? searchBatch = null;

        bus.Setup(b => b.PublishAsync("ls-storage-writer", It.IsAny<IReadOnlyList<LogEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<LogEntry>, CancellationToken>((_, batch, _) => storageBatch = batch)
            .Returns(Task.CompletedTask);
        bus.Setup(b => b.PublishAsync("ls-search-indexer", It.IsAny<IReadOnlyList<LogEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<LogEntry>, CancellationToken>((_, batch, _) => searchBatch = batch)
            .Returns(Task.CompletedTask);

        var handler = new IngestLogsHandler(bus.Object, NullLogger<IngestLogsHandler>.Instance);
        var command = new IngestLogsCommand
        {
            TenantId = "tenant-1",
            Entries = new[] { CreateDto(), CreateDto(message: "ok 2") }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.Accepted.Should().Be(2);
        result.Rejected.Should().Be(0);
        result.BatchId.Should().NotBeNullOrWhiteSpace();
        storageBatch.Should().NotBeNull();
        searchBatch.Should().NotBeNull();
        storageBatch.Should().HaveCount(2);
        searchBatch.Should().BeSameAs(storageBatch);
    }

    [Fact]
    public async Task Handle_WithInvalidEntries_DoesNotPublishAndCountsRejectedEntries()
    {
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        var handler = new IngestLogsHandler(bus.Object, NullLogger<IngestLogsHandler>.Instance);
        var command = new IngestLogsCommand
        {
            TenantId = "tenant-1",
            Entries = new[] { CreateDto(logType: "not-a-log-type") }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.Accepted.Should().Be(0);
        result.Rejected.Should().Be(1);
        bus.VerifyNoOtherCalls();
    }

    private static LogEntryDto CreateDto(
        string logType = "Application",
        string message = "ok") =>
        new()
        {
            SourceId = "payment-service",
            LogType = logType,
            Level = "Information",
            Environment = "production",
            Category = "api",
            Timestamp = DateTimeOffset.UtcNow,
            Message = message
        };
}
