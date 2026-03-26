namespace Logs2Obs.Core.Graphs;

using Logs2Obs.Core.Models;

public static class VegaLiteSpecBuilder
{
    private const string SchemaUrl = "https://vega.github.io/schema/vega-lite/v5.json";

    public static object Build(GraphType graphType, QueryResultSchema schema, IList<Dictionary<string, object>> results) =>
        graphType switch
        {
            GraphType.LineChart => BuildLineChart(schema, results),
            GraphType.BarChart => BuildBarChart(schema, results),
            GraphType.AreaChart => BuildAreaChart(schema, results),
            GraphType.PieChart => BuildPieChart(schema, results),
            GraphType.HeatMap => BuildHeatMap(schema, results),
            GraphType.Scatter => BuildScatter(schema, results),
            GraphType.Stat => BuildStat(schema, results),
            GraphType.Gauge => BuildGauge(schema, results),
            GraphType.StackedAreaChart => BuildStackedAreaChart(schema, results),
            _ => BuildTableFallback(schema, results)
        };

    private static Dictionary<string, object?> BuildLineChart(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var time = GetTimestampColumn(schema);
        var numeric = GetFirstNumericColumn(schema);
        if (time is null || numeric is null)
            return BuildTableFallback(schema, results);

        return BuildSpec(
            results,
            "line",
            new Dictionary<string, object?>
            {
                ["x"] = new { field = time.Name, type = "temporal" },
                ["y"] = new { field = numeric.Name, type = "quantitative" }
            });
    }

    private static Dictionary<string, object?> BuildBarChart(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var category = GetCategoricalColumn(schema);
        var numeric = GetFirstNumericColumn(schema);
        if (category is null || numeric is null)
            return BuildTableFallback(schema, results);

        return BuildSpec(
            results,
            "bar",
            new Dictionary<string, object?>
            {
                ["x"] = new { field = category.Name, type = "nominal" },
                ["y"] = new { field = numeric.Name, type = "quantitative" }
            });
    }

    private static Dictionary<string, object?> BuildAreaChart(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var time = GetTimestampColumn(schema);
        var numeric = GetFirstNumericColumn(schema);
        if (time is null || numeric is null)
            return BuildTableFallback(schema, results);

        return BuildSpec(
            results,
            "area",
            new Dictionary<string, object?>
            {
                ["x"] = new { field = time.Name, type = "temporal" },
                ["y"] = new { field = numeric.Name, type = "quantitative" }
            });
    }

    private static Dictionary<string, object?> BuildPieChart(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var category = GetCategoricalColumn(schema);
        var numeric = GetFirstNumericColumn(schema);
        if (category is null || numeric is null)
            return BuildTableFallback(schema, results);

        return BuildSpec(
            results,
            "arc",
            new Dictionary<string, object?>
            {
                ["theta"] = new { field = numeric.Name, type = "quantitative" },
                ["color"] = new { field = category.Name, type = "nominal" }
            });
    }

    private static Dictionary<string, object?> BuildHeatMap(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var x = GetColumn(schema, 0);
        var y = GetColumn(schema, 1);
        var numeric = GetFirstNumericColumn(schema);
        if (x is null || y is null || numeric is null)
            return BuildTableFallback(schema, results);

        return BuildSpec(
            results,
            "rect",
            new Dictionary<string, object?>
            {
                ["x"] = new { field = x.Name, type = GetVegaType(x) },
                ["y"] = new { field = y.Name, type = GetVegaType(y) },
                ["color"] = new { field = numeric.Name, type = "quantitative" }
            });
    }

    private static Dictionary<string, object?> BuildScatter(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var numeric = GetNumericColumns(schema);
        if (numeric.Count < 2)
            return BuildTableFallback(schema, results);

        return BuildSpec(
            results,
            "point",
            new Dictionary<string, object?>
            {
                ["x"] = new { field = numeric[0].Name, type = "quantitative" },
                ["y"] = new { field = numeric[1].Name, type = "quantitative" }
            });
    }

    private static Dictionary<string, object?> BuildStat(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var numeric = GetFirstNumericColumn(schema);
        if (numeric is null)
            return BuildTableFallback(schema, results);

        return BuildSpec(
            results,
            new { type = "text", fontSize = 48 },
            new Dictionary<string, object?>
            {
                ["text"] = new { aggregate = "sum", field = numeric.Name, type = "quantitative" }
            });
    }

    private static Dictionary<string, object?> BuildGauge(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var numeric = GetFirstNumericColumn(schema);
        if (numeric is null)
            return BuildTableFallback(schema, results);

        return BuildSpec(
            results,
            "arc",
            new Dictionary<string, object?>
            {
                ["theta"] = new { aggregate = "sum", field = numeric.Name, type = "quantitative" }
            });
    }

    private static Dictionary<string, object?> BuildStackedAreaChart(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var time = GetTimestampColumn(schema);
        var numeric = GetFirstNumericColumn(schema);
        var category = GetCategoricalColumn(schema);
        if (time is null || numeric is null || category is null)
            return BuildTableFallback(schema, results);

        return BuildSpec(
            results,
            new { type = "area", stack = true },
            new Dictionary<string, object?>
            {
                ["x"] = new { field = time.Name, type = "temporal" },
                ["y"] = new { field = numeric.Name, type = "quantitative" },
                ["color"] = new { field = category.Name, type = "nominal" }
            });
    }

    private static Dictionary<string, object?> BuildSpec(IList<Dictionary<string, object>> results, object mark, Dictionary<string, object?> encoding)
    {
        var spec = new Dictionary<string, object?>
        {
            ["$schema"] = SchemaUrl,
            ["width"] = 800,
            ["height"] = 400,
            ["data"] = new { values = results },
            ["mark"] = mark,
            ["encoding"] = encoding
        };

        return spec;
    }

    private static Dictionary<string, object?> BuildTableFallback(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var column = schema.Columns.FirstOrDefault();
        var encoding = column is null
            ? new Dictionary<string, object?> { ["text"] = new { value = "No data" } }
            : new Dictionary<string, object?> { ["text"] = new { field = column.Name, type = GetVegaType(column) } };

        return BuildSpec(results, "text", encoding);
    }

    private static ColumnInfo? GetTimestampColumn(QueryResultSchema schema) =>
        schema.Columns.FirstOrDefault(c =>
            c.IsTimestamp ||
            c.Name.Contains("time", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("date", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("bucket", StringComparison.OrdinalIgnoreCase));

    private static ColumnInfo? GetCategoricalColumn(QueryResultSchema schema) =>
        schema.Columns.FirstOrDefault(c => c.IsCategorical || (!c.IsNumeric && !c.IsTimestamp));

    private static ColumnInfo? GetFirstNumericColumn(QueryResultSchema schema) =>
        schema.Columns.FirstOrDefault(c => c.IsNumeric);

    private static List<ColumnInfo> GetNumericColumns(QueryResultSchema schema) =>
        schema.Columns.Where(c => c.IsNumeric).ToList();

    private static ColumnInfo? GetColumn(QueryResultSchema schema, int index) =>
        schema.Columns.Count > index ? schema.Columns[index] : null;

    private static string GetVegaType(ColumnInfo column) =>
        column.IsTimestamp ? "temporal" : column.IsNumeric ? "quantitative" : "nominal";
}
