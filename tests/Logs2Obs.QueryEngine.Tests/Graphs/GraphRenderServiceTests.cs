namespace Logs2Obs.QueryEngine.Tests.Graphs;

using Logs2Obs.Core.Graphs;
using Logs2Obs.QueryEngine.Graphs;

public class GraphRenderServiceTests
{
    [Fact]
    public async Task RenderAsync_WhenAutoSelect_UsesGraphSuggestionEngine()
    {
        var service = CreateService();
        var schema = BuildSchema();
        var results = BuildResults();

        var output = await service.RenderAsync(new GraphRenderRequest
        {
            QueryId = "query-1",
            TenantId = "tenant-1",
            GraphType = GraphType.LineChart,
            AutoSelect = true,
            Schema = schema,
            Results = results
        }, CancellationToken.None);

        output.Should().NotBeNull();
        output.Type.Should().Be(GraphType.AreaChart.ToString());
    }

    [Fact]
    public async Task RenderAsync_WhenGraphTypeSpecified_BuildsBothSpecs()
    {
        var service = CreateService();
        var schema = BuildSchema();
        var results = BuildResults();

        var output = await service.RenderAsync(new GraphRenderRequest
        {
            QueryId = "query-1",
            TenantId = "tenant-1",
            GraphType = GraphType.LineChart,
            Schema = schema,
            Results = results
        }, CancellationToken.None);

        output.Should().NotBeNull();
        output.VegaLiteSpec.Should().NotBeNull();
        output.ChartJsConfig.Should().NotBeNull();
    }

    [Fact]
    public async Task RenderAsync_WhenResultsEmpty_ReturnsValidSpecs()
    {
        var service = CreateService();
        var schema = BuildSchema();

        var output = await service.RenderAsync(new GraphRenderRequest
        {
            QueryId = "query-1",
            TenantId = "tenant-1",
            GraphType = GraphType.LineChart,
            Schema = schema,
            Results = new List<Dictionary<string, object>>()
        }, CancellationToken.None);

        output.Should().NotBeNull();
        output.VegaLiteSpec.Should().NotBeNull();
        output.ChartJsConfig.Should().NotBeNull();
    }

    [Fact]
    public async Task RenderAsync_WhenSchemaHasNoColumns_FallsBack()
    {
        var service = CreateService();
        var schema = new QueryResultSchema();

        var output = await service.RenderAsync(new GraphRenderRequest
        {
            QueryId = "query-1",
            TenantId = "tenant-1",
            GraphType = GraphType.LineChart,
            Schema = schema,
            Results = BuildResults()
        }, CancellationToken.None);

        output.Should().NotBeNull();
        output.VegaLiteSpec.Should().NotBeNull();
        output.ChartJsConfig.Should().NotBeNull();
    }

    private static GraphRenderService CreateService() =>
        new(new GraphSuggestionEngine(), Mock.Of<IAiService>(), NullLogger<GraphRenderService>.Instance);

    private static QueryResultSchema BuildSchema() =>
        new()
        {
            Columns =
            [
                new ColumnInfo { Name = "timestamp", DataType = "timestamp", IsTimestamp = true },
                new ColumnInfo { Name = "count", DataType = "double", IsNumeric = true }
            ],
            RowCount = 3
        };

    private static List<Dictionary<string, object>> BuildResults() =>
        [
            new Dictionary<string, object>
            {
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["count"] = 42d
            }
        ];
}
