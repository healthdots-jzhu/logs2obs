namespace Logs2Obs.QueryEngine.Tests.AI;

using Logs2Obs.Core.Commands;
using Logs2Obs.Core.Handlers;
using Logs2Obs.QueryEngine.AI;

public class NaturalLanguageQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenAiServiceReturnsValidSql_ReturnsAiSqlResult()
    {
        var aiService = new Mock<IAiService>();
        aiService.Setup(service => service.TranslateToSqlAsync(
                It.IsAny<string>(),
                It.IsAny<QueryContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NlQueryResult
            {
                Sql = "SELECT * FROM logs WHERE year = 2024 LIMIT 1",
                Explanation = "test",
                SuggestedGraphType = GraphType.LineChart
            });

        var handler = new GetNaturalLanguageQueryHandler(
            aiService.Object,
            new SqlSafetyValidator(),
            NullLogger<GetNaturalLanguageQueryHandler>.Instance);
        var command = new GetNaturalLanguageQuery { TenantId = "tenant-1", NaturalLanguage = "show errors" };

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Sql.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Handle_WhenSqlFailsSafety_ThrowsSqlSafetyException()
    {
        var aiService = new Mock<IAiService>();
        aiService.Setup(service => service.TranslateToSqlAsync(
                It.IsAny<string>(),
                It.IsAny<QueryContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NlQueryResult
            {
                Sql = "DROP TABLE users",
                Explanation = "bad",
                SuggestedGraphType = GraphType.LineChart
            });

        var handler = new GetNaturalLanguageQueryHandler(
            aiService.Object,
            new SqlSafetyValidator(),
            NullLogger<GetNaturalLanguageQueryHandler>.Instance);
        var command = new GetNaturalLanguageQuery { TenantId = "tenant-1", NaturalLanguage = "drop users" };

        Func<Task> act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<SqlSafetyException>();
    }

    [Fact]
    public async Task Handle_WhenAiServiceIsNoOp_ReturnsPlaceholder()
    {
        var handler = new GetNaturalLanguageQueryHandler(
            new NoOpAiService(),
            new SqlSafetyValidator(),
            NullLogger<GetNaturalLanguageQueryHandler>.Instance);
        var command = new GetNaturalLanguageQuery { TenantId = "tenant-1", NaturalLanguage = "placeholder" };

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Sql.Should().Be("SELECT 1");
    }

    [Fact]
    public async Task Handle_WithTenantContext_BuildsQueryContext()
    {
        QueryContext? captured = null;
        var aiService = new Mock<IAiService>();
        aiService.Setup(service => service.TranslateToSqlAsync(
                It.IsAny<string>(),
                It.IsAny<QueryContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, QueryContext, CancellationToken>((_, ctx, _) => captured = ctx)
            .ReturnsAsync(new NlQueryResult
            {
                Sql = "SELECT * FROM logs WHERE year = 2024 LIMIT 1",
                Explanation = "test",
                SuggestedGraphType = GraphType.LineChart
            });

        var handler = new GetNaturalLanguageQueryHandler(
            aiService.Object,
            new SqlSafetyValidator(),
            NullLogger<GetNaturalLanguageQueryHandler>.Instance);
        var command = new GetNaturalLanguageQuery { TenantId = "tenant-1", NaturalLanguage = "errors by service" };

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be("tenant-1");
    }
}
