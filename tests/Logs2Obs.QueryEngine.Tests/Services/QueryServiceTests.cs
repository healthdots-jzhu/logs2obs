namespace Logs2Obs.QueryEngine.Tests.Services;

using Logs2Obs.QueryEngine.Services;
using Logs2Obs.QueryEngine.Telemetry;
using Logs2Obs.QueryEngine.Tests.Helpers;
using Microsoft.Extensions.Logging;
using System.Net.Http;

public class QueryServiceTests
{
    private const string TenantId = "tenant-1";

    [Fact]
    public async Task ExecuteAsync_WhenSqlIsInvalid_ThrowsSqlSafetyException()
    {
        var queryEngine = new Mock<IQueryEngine>(MockBehavior.Strict);
        var metadataStore = new Mock<IMetadataStore>(MockBehavior.Strict);
        var service = CreateService(queryEngine, metadataStore, new SqlSafetyValidator());
        var cmd = TestDataBuilders.AValidExecuteSqlQuery("SELECT * FROM logs; DROP TABLE users;");

        var act = () => service.ExecuteAsync(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<SqlSafetyException>();
        queryEngine.VerifyNoOtherCalls();
        metadataStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenHotTier_RoutesToQueryEngine()
    {
        var now = DateTimeOffset.UtcNow;
        var sql = BuildSql(now.AddDays(-2), now.AddDays(-1));
        var estimate = BuildEstimate(0.01);

        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.EstimateCostAsync(sql, It.IsAny<CancellationToken>()))
            .ReturnsAsync(estimate);
        queryEngine.Setup(x => x.SubmitAsync(TenantId, sql, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed, ExecutionId = "exec-1" });

        var metadataStore = CreateMetadataStore();
        var service = CreateService(queryEngine, metadataStore);
        var cmd = TestDataBuilders.AValidExecuteSqlQuery(sql);

        var result = await service.ExecuteAsync(cmd, CancellationToken.None);

        result.Status.Should().Be(QueryStatus.Completed);
        result.Estimate.Should().Be(estimate);
        queryEngine.Verify(x => x.SubmitAsync(TenantId, sql, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenWarmTier_EstimatesCostFirst()
    {
        var now = DateTimeOffset.UtcNow;
        var sql = BuildSql(now.AddDays(-30), now.AddDays(-8));
        var estimate = BuildEstimate(0.01);
        var sequence = new MockSequence();

        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.InSequence(sequence)
            .Setup(x => x.EstimateCostAsync(sql, It.IsAny<CancellationToken>()))
            .ReturnsAsync(estimate);
        queryEngine.InSequence(sequence)
            .Setup(x => x.SubmitAsync(TenantId, sql, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed, ExecutionId = "exec-2" });

        var metadataStore = CreateMetadataStore();
        var service = CreateService(queryEngine, metadataStore);
        var cmd = TestDataBuilders.AValidExecuteSqlQuery(sql);

        var result = await service.ExecuteAsync(cmd, CancellationToken.None);

        result.Status.Should().Be(QueryStatus.Completed);
        queryEngine.Verify(x => x.EstimateCostAsync(sql, It.IsAny<CancellationToken>()), Times.Once);
        queryEngine.Verify(x => x.SubmitAsync(TenantId, sql, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCostExceedsThreshold_ReturnsPendingConfirmation()
    {
        var now = DateTimeOffset.UtcNow;
        var sql = BuildSql(now.AddDays(-2), now.AddDays(-1));
        var estimate = BuildEstimate(5.0);

        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.EstimateCostAsync(sql, It.IsAny<CancellationToken>()))
            .ReturnsAsync(estimate);

        var metadataStore = CreateMetadataStore();
        var service = CreateService(queryEngine, metadataStore);
        var cmd = TestDataBuilders.AValidExecuteSqlQuery(sql) with { ConfirmCostIfAboveUsd = 0.05 };

        var result = await service.ExecuteAsync(cmd, CancellationToken.None);

        result.Status.Should().Be(QueryStatus.PendingCostConfirmation);
        result.Estimate.Should().Be(estimate);
        queryEngine.Verify(x => x.SubmitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCrossTier_FansOutToMultipleTiers()
    {
        var now = DateTimeOffset.UtcNow;
        var sql = BuildSql(now.AddDays(-30), now.AddDays(-2));
        var estimate = BuildEstimate(0.01);

        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.EstimateCostAsync(sql, It.IsAny<CancellationToken>()))
            .ReturnsAsync(estimate);
        queryEngine.SetupSequence(x => x.SubmitAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed, ExecutionId = "exec-3" })
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed, ExecutionId = "exec-4" });

        var metadataStore = CreateMetadataStore();
        var service = CreateService(queryEngine, metadataStore);
        var cmd = TestDataBuilders.AValidExecuteSqlQuery(sql);

        var result = await service.ExecuteAsync(cmd, CancellationToken.None);

        result.Status.Should().Be(QueryStatus.Completed);
        queryEngine.Verify(x => x.SubmitAsync(TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_WhenQueryEngineThrows_RetriesWithPolly()
    {
        var now = DateTimeOffset.UtcNow;
        var sql = BuildSql(now.AddDays(-2), now.AddDays(-1));
        var estimate = BuildEstimate(0.01);

        var queryEngine = new Mock<IQueryEngine>();
        queryEngine.Setup(x => x.EstimateCostAsync(sql, It.IsAny<CancellationToken>()))
            .ReturnsAsync(estimate);
        queryEngine.SetupSequence(x => x.SubmitAsync(TenantId, sql, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("transient"))
            .ThrowsAsync(new HttpRequestException("transient"))
            .ReturnsAsync(new QuerySubmitResult { Status = QueryStatus.Completed, ExecutionId = "exec-5" });

        var metadataStore = CreateMetadataStore();
        var service = CreateService(queryEngine, metadataStore);
        var cmd = TestDataBuilders.AValidExecuteSqlQuery(sql);

        var result = await service.ExecuteAsync(cmd, CancellationToken.None);

        result.Status.Should().Be(QueryStatus.Completed);
        queryEngine.Verify(x => x.SubmitAsync(TenantId, sql, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    private static QueryService CreateService(
        Mock<IQueryEngine> queryEngine,
        Mock<IMetadataStore> metadataStore,
        ISqlSafetyValidator? safetyValidator = null)
    {
        var router = new QueryTierRouter(NullLogger<QueryTierRouter>.Instance);
        var metrics = new QueryEngineMetrics();
        var logger = new Mock<ILogger<QueryService>>();
        return new QueryService(
            queryEngine.Object,
            safetyValidator ?? new SqlSafetyValidator(),
            router,
            metadataStore.Object,
            metrics,
            logger.Object);
    }

    private static Mock<IMetadataStore> CreateMetadataStore()
    {
        var metadataStore = new Mock<IMetadataStore>();
        metadataStore.Setup(x => x.GetAsync<TenantSettings>("tenant_settings", TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestDataBuilders.AValidTenantSettings());
        return metadataStore;
    }

    private static string BuildSql(DateTimeOffset from, DateTimeOffset to) =>
        $"SELECT * FROM logs WHERE timestamp >= '{from:O}' AND timestamp < '{to:O}' LIMIT 100";

    private static QueryCostEstimate BuildEstimate(double estimatedCostUsd) => new()
    {
        EstimatedScanGb = 1.0,
        EstimatedCostUsd = estimatedCostUsd,
        ConfidenceLevel = "high"
    };
}
