using FluentAssertions;
using Logs2Obs.Core.Graphs;

namespace Logs2Obs.Core.Tests.Graphs;

public class GraphSuggestionEngineTests
{
    private readonly GraphSuggestionEngine _sut = new();

    // --- AreaChart: hasTime && hasCount ---

    [Fact]
    public void SuggestFromSchema_WhenSchemaHasTimeAndCount_SuggestsAreaChart()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("timestamp", ColumnType.Timestamp)
            .WithColumn("count", ColumnType.Numeric)
            .WithRowCount(100)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.AreaChart);
    }

    // --- LineChart: hasTime && !hasCount ---

    [Fact]
    public void SuggestFromSchema_WhenSchemaHasTimeButNoCount_SuggestsLineChart()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("timestamp", ColumnType.Timestamp)
            .WithColumn("duration_ms", ColumnType.Numeric)
            .WithRowCount(50)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.LineChart);
    }

    [Fact]
    public void SuggestFromSchema_WhenSchemaHasTimeAndCount_DoesNotSuggestLineChart()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("timestamp", ColumnType.Timestamp)
            .WithColumn("count", ColumnType.Numeric)
            .WithRowCount(50)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        // When count exists, AreaChart is preferred over LineChart
        suggestions.Should().NotContain(s => s.GraphType == GraphType.LineChart);
    }

    // --- GroupedBarChart: hasP99 ---

    [Fact]
    public void SuggestFromSchema_WhenSchemaHasP99Column_SuggestsGroupedBarChart()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("service", ColumnType.Categorical)
            .WithColumn("p99_ms", ColumnType.Numeric)
            .WithRowCount(10)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.GroupedBarChart);
    }

    [Fact]
    public void SuggestFromSchema_WhenSchemaHasP90Column_SuggestsGroupedBarChart()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("p90_ms", ColumnType.Numeric)
            .WithRowCount(10)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.GroupedBarChart);
    }

    // --- Heatmap: hasHourDay && hasCount ---

    [Fact]
    public void SuggestFromSchema_WhenSchemaHasHourOfDayAndDayOfWeekAndCount_SuggestsHeatmap()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("hour_of_day", ColumnType.Numeric)
            .WithColumn("day_of_week", ColumnType.Categorical)
            .WithColumn("count", ColumnType.Numeric)
            .WithRowCount(168)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.Heatmap);
    }

    // --- Gauge: isSingleRow && isSingleNum ---

    [Fact]
    public void SuggestFromSchema_WhenSingleRowAndSingleNumericColumn_SuggestsGauge()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("error_rate", ColumnType.Numeric)
            .WithRowCount(1)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.Gauge);
    }

    // --- DonutChart: isSmallCat && hasCount ---

    [Fact]
    public void SuggestFromSchema_WhenSmallCategoricalWithCount_SuggestsDonutChart()
    {
        // RowCount 2–10, categorical + count
        var schema = SchemaBuilder.New()
            .WithColumn("logtype", ColumnType.Categorical)
            .WithColumn("count", ColumnType.Numeric)
            .WithRowCount(7)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.DonutChart);
    }

    // --- HorizontalBarChart: hasCat && hasCount && !hasTime ---

    [Fact]
    public void SuggestFromSchema_WhenCategoricalWithCountAndNoTime_SuggestsHorizontalBarChart()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("service", ColumnType.Categorical)
            .WithColumn("error_count", ColumnType.Numeric)
            .WithRowCount(20)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.HorizontalBarChart);
    }

    [Fact]
    public void SuggestFromSchema_WhenCategoricalWithCountAndTime_DoesNotSuggestHorizontalBarChart()
    {
        // Adding a time column disqualifies HorizontalBarChart
        var schema = SchemaBuilder.New()
            .WithColumn("timestamp", ColumnType.Timestamp)
            .WithColumn("service", ColumnType.Categorical)
            .WithColumn("error_count", ColumnType.Numeric)
            .WithRowCount(20)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().NotContain(s => s.GraphType == GraphType.HorizontalBarChart);
    }

    // --- ScatterPlot: hasCorrelation ---

    [Fact]
    public void SuggestFromSchema_WhenSchemHasDurationMsAndRequestBytes_SuggestsScatterPlot()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("duration_ms", ColumnType.Numeric)
            .WithColumn("request_bytes", ColumnType.Numeric)
            .WithRowCount(500)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.ScatterPlot);
    }

    [Fact]
    public void SuggestFromSchema_WhenSchemaHasP99MsAndErrorCount_SuggestsScatterPlot()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("p99_ms", ColumnType.Numeric)
            .WithColumn("error_count", ColumnType.Numeric)
            .WithRowCount(100)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.ScatterPlot);
    }

    // --- StackedAreaChart: hasTime && hasCat ---

    [Fact]
    public void SuggestFromSchema_WhenSchemaHasTimeAndCategorical_SuggestsStackedAreaChart()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("timestamp", ColumnType.Timestamp)
            .WithColumn("logtype", ColumnType.Categorical)
            .WithColumn("count", ColumnType.Numeric)
            .WithRowCount(200)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().Contain(s => s.GraphType == GraphType.StackedAreaChart);
    }

    // --- Ordering ---

    [Fact]
    public void SuggestFromSchema_SuggestionsAreOrderedByPriority()
    {
        var schema = SchemaBuilder.New()
            .WithColumn("timestamp", ColumnType.Timestamp)
            .WithColumn("logtype", ColumnType.Categorical)
            .WithColumn("count", ColumnType.Numeric)
            .WithRowCount(100)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        var priorities = suggestions.Select(s => s.Priority).ToList();
        priorities.Should().BeInAscendingOrder();
    }

    [Fact]
    public void SuggestFromSchema_WhenEmptySchema_ReturnsEmptySuggestions()
    {
        var schema = SchemaBuilder.New()
            .WithRowCount(0)
            .Build();

        var suggestions = _sut.SuggestFromSchema(schema);

        suggestions.Should().BeEmpty();
    }
}

/// <summary>
/// Fluent builder for constructing QueryResultSchema test doubles.
/// Assumes QueryResultSchema has a public constructor accepting columns + row count.
/// </summary>
file static class SchemaBuilder
{
    public static SchemaBuilderContext New() => new();
}

file sealed class SchemaBuilderContext
{
    private readonly List<QueryColumn> _columns = [];
    private int _rowCount;

    public SchemaBuilderContext WithColumn(string name, ColumnType type)
    {
        _columns.Add(new QueryColumn(name, type));
        return this;
    }

    public SchemaBuilderContext WithRowCount(int rowCount)
    {
        _rowCount = rowCount;
        return this;
    }

    public QueryResultSchema Build() => new(_columns, _rowCount);
}
