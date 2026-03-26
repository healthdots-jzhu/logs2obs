namespace Logs2Obs.QueryEngine.Tests.MatViews;

using System.Collections.Concurrent;
using Logs2Obs.Core.MatViews;
using Logs2Obs.QueryEngine.MatViews;

public class MatViewRefreshServiceTests
{
    private const string TenantId = "tenant-1";

    [Fact]
    public async Task RefreshAllForTenant_CallsRefreshForEachView()
    {
        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.SubmitAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed });

        var refreshed = new ConcurrentBag<string>();
        var matViewEngine = new Mock<IMatViewEngine>();
        matViewEngine.Setup(x => x.RefreshAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, view, _) => refreshed.Add(view))
            .Returns(Task.CompletedTask);

        var service = new MatViewRefreshService(
            matViewEngine.Object,
            queryEngine.Object,
            NullLogger<MatViewRefreshService>.Instance);

        await service.RefreshAllForTenantAsync(TenantId, CancellationToken.None);

        refreshed.Should().BeEquivalentTo(StandardMatViews.All.Select(v => v.Name));
    }

    [Fact]
    public async Task RefreshAllForTenant_ExecutesViewSql()
    {
        var sqlCalls = new ConcurrentBag<string>();
        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.SubmitAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, sql, _) => sqlCalls.Add(sql))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed });

        var matViewEngine = new Mock<IMatViewEngine>();
        matViewEngine.Setup(x => x.RefreshAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new MatViewRefreshService(
            matViewEngine.Object,
            queryEngine.Object,
            NullLogger<MatViewRefreshService>.Instance);

        await service.RefreshAllForTenantAsync(TenantId, CancellationToken.None);

        sqlCalls.Should().HaveCount(StandardMatViews.All.Count);
        sqlCalls.Should().OnlyContain(sql => sql.Contains($"tenantId='{TenantId}'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAllForTenant_InjectsTenantFilter()
    {
        var sqlCalls = new ConcurrentBag<string>();
        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.SubmitAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, sql, _) => sqlCalls.Add(sql))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed });

        var matViewEngine = new Mock<IMatViewEngine>();
        matViewEngine.Setup(x => x.RefreshAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new MatViewRefreshService(
            matViewEngine.Object,
            queryEngine.Object,
            NullLogger<MatViewRefreshService>.Instance);

        await service.RefreshAllForTenantAsync(TenantId, CancellationToken.None);

        sqlCalls.Should().OnlyContain(sql => !sql.Contains("{TENANT_FILTER}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAllForTenant_WhenEngineThrows_PropagatesException()
    {
        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.SubmitAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<MatViewRefreshService>>();
        var service = new MatViewRefreshService(
            new Mock<IMatViewEngine>().Object,
            queryEngine.Object,
            logger.Object);

        var act = () => service.RefreshAllForTenantAsync(TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        logger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
