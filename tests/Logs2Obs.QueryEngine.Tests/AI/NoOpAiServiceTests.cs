namespace Logs2Obs.QueryEngine.Tests.AI;

using Logs2Obs.Core.Graphs;
using Logs2Obs.Core.Models;
using Logs2Obs.QueryEngine.AI;

public class NoOpAiServiceTests
{
    [Fact]
    public async Task GenerateSqlAsync_ReturnsPlaceholderResult()
    {
        var service = new NoOpAiService();

        var result = await service.GenerateSqlAsync("tenant-1", "show errors", "schema", CancellationToken.None);

        result.Should().NotBeNull();
        result.Sql.Should().Be("SELECT 1");
    }

    [Fact]
    public async Task TranslateToSqlAsync_ReturnsPlaceholderResult()
    {
        var service = new NoOpAiService();
        var context = new QueryContext { TenantId = "tenant-1" };

        var result = await service.TranslateToSqlAsync("show errors", context, CancellationToken.None);

        result.Should().NotBeNull();
        result.Sql.Should().Be("SELECT 1");
        Enum.IsDefined(typeof(GraphType), result.SuggestedGraphType).Should().BeTrue();
    }

    [Fact]
    public async Task SuggestGraphsAsync_ReturnsEmptyList()
    {
        var service = new NoOpAiService();
        var schema = new QueryResultSchema();

        var result = await service.SuggestGraphsAsync(schema, null, CancellationToken.None);

        result.Should().BeEmpty();
    }
}
