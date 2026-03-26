namespace Logs2Obs.QueryEngine.Tests.Graphs;

using Logs2Obs.Core.Graphs;

public class GraphSuggestionEngineTests
{
    [Fact]
    public void SuggestFromSchema_WhenHasTimestampAndNumeric_SuggestsLineChart()
    {
        var schema = BuildSchema(
            new ColumnInfo { Name = "timestamp", DataType = "timestamp", IsTimestamp = true },
            new ColumnInfo { Name = "value", DataType = "double", IsNumeric = true });

        var suggestions = GraphSuggestionEngine.SuggestFromSchema(schema);

        suggestions.Should().ContainSingle(s => s.GraphType == GraphType.LineChart);
    }

    [Fact]
    public void SuggestFromSchema_WhenHasCategoryAndNumeric_SuggestsBarChart()
    {
        var schema = BuildSchema(
            new ColumnInfo { Name = "level", DataType = "string", IsCategorical = true },
            new ColumnInfo { Name = "count", DataType = "int", IsNumeric = true });

        var suggestions = GraphSuggestionEngine.SuggestFromSchema(schema);

        suggestions.Should().ContainSingle(s => s.GraphType == GraphType.BarChart);
    }

    [Fact]
    public void SuggestFromSchema_WhenHasTwoNumericColumns_SuggestsScatter()
    {
        var schema = BuildSchema(
            new ColumnInfo { Name = "duration_ms", DataType = "double", IsNumeric = true },
            new ColumnInfo { Name = "request_bytes", DataType = "double", IsNumeric = true });

        var suggestions = GraphSuggestionEngine.SuggestFromSchema(schema);

        suggestions.Should().ContainSingle(s => s.GraphType == GraphType.Scatter);
    }

    [Fact]
    public void SuggestFromSchema_WhenHasSingleNumericColumn_SuggestsStat()
    {
        var schema = BuildSchema(
            1,
            new ColumnInfo { Name = "latency_ms", DataType = "double", IsNumeric = true });

        var suggestions = GraphSuggestionEngine.SuggestFromSchema(schema);

        suggestions.Should().ContainSingle(s => s.GraphType == GraphType.Gauge);
    }

    [Fact]
    public void SuggestFromSchema_WhenHasTimestampAndCategoryAndNumeric_SuggestsStackedAreaChart()
    {
        var schema = BuildSchema(
            new ColumnInfo { Name = "timestamp", DataType = "timestamp", IsTimestamp = true },
            new ColumnInfo { Name = "service", DataType = "string", IsCategorical = true },
            new ColumnInfo { Name = "count", DataType = "int", IsNumeric = true });

        var suggestions = GraphSuggestionEngine.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.StackedAreaChart);
    }

    [Fact]
    public void SuggestFromSchema_WhenSchemaEmpty_ReturnsDefaultSuggestion()
    {
        var suggestions = GraphSuggestionEngine.SuggestFromSchema(new QueryResultSchema());

        suggestions.Should().BeEmpty();
    }

    [Fact]
    public void SuggestFromSchema_ReturnsOrderedByConfidence()
    {
        var schema = BuildSchema(
            new ColumnInfo { Name = "timestamp", DataType = "timestamp", IsTimestamp = true },
            new ColumnInfo { Name = "count", DataType = "int", IsNumeric = true },
            new ColumnInfo { Name = "service", DataType = "string", IsCategorical = true });

        var suggestions = GraphSuggestionEngine.SuggestFromSchema(schema);

        suggestions.Select(s => s.Confidence).Should().BeInDescendingOrder();
        suggestions[0].GraphType.Should().Be(GraphType.AreaChart);
    }

    private static QueryResultSchema BuildSchema(params ColumnInfo[] columns) =>
        BuildSchema(10, columns);

    private static QueryResultSchema BuildSchema(int rowCount, params ColumnInfo[] columns) =>
        new()
        {
            Columns = columns.ToList(),
            RowCount = rowCount
        };
}
