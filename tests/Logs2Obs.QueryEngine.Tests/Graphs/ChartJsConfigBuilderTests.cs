namespace Logs2Obs.QueryEngine.Tests.Graphs;

using Logs2Obs.Core.Graphs;
using System.Text.Json;

public class ChartJsConfigBuilderTests
{
    [Fact]
    public void Build_LineChart_ReturnsValidConfig() =>
        AssertConfig(GraphType.LineChart);

    [Fact]
    public void Build_BarChart_ReturnsValidConfig() =>
        AssertConfig(GraphType.BarChart);

    [Fact]
    public void Build_AreaChart_ReturnsValidConfig() =>
        AssertConfig(GraphType.AreaChart);

    [Fact]
    public void Build_PieChart_ReturnsValidConfig() =>
        AssertConfig(GraphType.PieChart);

    [Fact]
    public void Build_HeatMap_ReturnsValidConfig() =>
        AssertConfig(GraphType.HeatMap);

    [Fact]
    public void Build_Scatter_ReturnsValidConfig() =>
        AssertConfig(GraphType.Scatter);

    [Fact]
    public void Build_Stat_ReturnsValidConfig() =>
        AssertConfig(GraphType.Stat);

    [Fact]
    public void Build_Gauge_ReturnsValidConfig() =>
        AssertConfig(GraphType.Gauge);

    [Fact]
    public void Build_StackedAreaChart_ReturnsValidConfig() =>
        AssertConfig(GraphType.StackedAreaChart);

    private static void AssertConfig(GraphType graphType)
    {
        var config = ChartJsConfigBuilder.Build(graphType, BuildSchema(graphType), BuildResults(graphType));

        config.Should().NotBeNull();
        var json = JsonSerializer.Serialize(config);
        json.Should().Contain("\"type\"");
    }

    private static QueryResultSchema BuildSchema(GraphType graphType)
    {
        var columns = graphType switch
        {
            GraphType.LineChart or GraphType.AreaChart => new List<ColumnInfo>
            {
                new() { Name = "timestamp", DataType = "timestamp", IsTimestamp = true },
                new() { Name = "count", DataType = "double", IsNumeric = true }
            },
            GraphType.BarChart or GraphType.PieChart => new List<ColumnInfo>
            {
                new() { Name = "service", DataType = "string", IsCategorical = true },
                new() { Name = "count", DataType = "double", IsNumeric = true }
            },
            GraphType.HeatMap => new List<ColumnInfo>
            {
                new() { Name = "hour", DataType = "string", IsCategorical = true },
                new() { Name = "day", DataType = "string", IsCategorical = true },
                new() { Name = "count", DataType = "double", IsNumeric = true }
            },
            GraphType.Scatter => new List<ColumnInfo>
            {
                new() { Name = "x", DataType = "double", IsNumeric = true },
                new() { Name = "y", DataType = "double", IsNumeric = true }
            },
            GraphType.Stat or GraphType.Gauge => new List<ColumnInfo>
            {
                new() { Name = "count", DataType = "double", IsNumeric = true }
            },
            GraphType.StackedAreaChart => new List<ColumnInfo>
            {
                new() { Name = "timestamp", DataType = "timestamp", IsTimestamp = true },
                new() { Name = "service", DataType = "string", IsCategorical = true },
                new() { Name = "count", DataType = "double", IsNumeric = true }
            },
            _ => []
        };

        return new QueryResultSchema { Columns = columns, RowCount = 1 };
    }

    private static IList<Dictionary<string, object>> BuildResults(GraphType graphType)
    {
        var row = graphType switch
        {
            GraphType.LineChart or GraphType.AreaChart => new Dictionary<string, object>
            {
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["count"] = 42d
            },
            GraphType.BarChart or GraphType.PieChart => new Dictionary<string, object>
            {
                ["service"] = "api",
                ["count"] = 7d
            },
            GraphType.HeatMap => new Dictionary<string, object>
            {
                ["hour"] = "10",
                ["day"] = "Mon",
                ["count"] = 3d
            },
            GraphType.Scatter => new Dictionary<string, object>
            {
                ["x"] = 1d,
                ["y"] = 2d
            },
            GraphType.Stat or GraphType.Gauge => new Dictionary<string, object>
            {
                ["count"] = 5d
            },
            GraphType.StackedAreaChart => new Dictionary<string, object>
            {
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["service"] = "api",
                ["count"] = 12d
            },
            _ => new Dictionary<string, object>()
        };

        return new List<Dictionary<string, object>> { row };
    }
}
