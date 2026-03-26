namespace Logs2Obs.Core.Graphs;

using System.Globalization;
using System.Text.Json;
using Logs2Obs.Core.Models;

public static class ChartJsConfigBuilder
{
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
            _ => BuildMinimalConfig()
        };

    private static object BuildLineChart(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var timeColumn = GetTimestampColumnName(schema);
        var numericColumns = GetNumericColumnNames(schema);
        if (timeColumn is null || numericColumns.Count == 0)
            return BuildMinimalConfig();

        var labels = results.Select(r => GetStringValue(r, timeColumn)).ToList();
        var datasets = numericColumns
            .Select(col => new
            {
                label = col,
                data = results.Select(r => GetNumericValue(r, col) ?? 0d).ToList()
            })
            .ToList();

        return BuildConfig("line", labels, datasets);
    }

    private static object BuildBarChart(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var category = GetCategoricalColumnName(schema);
        var numericColumns = GetNumericColumnNames(schema);
        if (category is null || numericColumns.Count == 0)
            return BuildMinimalConfig();

        var labels = results.Select(r => GetStringValue(r, category)).ToList();
        var datasets = numericColumns
            .Select(col => new
            {
                label = col,
                data = results.Select(r => GetNumericValue(r, col) ?? 0d).ToList()
            })
            .ToList();

        return BuildConfig("bar", labels, datasets);
    }

    private static object BuildAreaChart(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var timeColumn = GetTimestampColumnName(schema);
        var numericColumns = GetNumericColumnNames(schema);
        if (timeColumn is null || numericColumns.Count == 0)
            return BuildMinimalConfig();

        var labels = results.Select(r => GetStringValue(r, timeColumn)).ToList();
        var datasets = numericColumns
            .Select(col => new
            {
                label = col,
                data = results.Select(r => GetNumericValue(r, col) ?? 0d).ToList(),
                fill = true
            })
            .ToList();

        return BuildConfig("line", labels, datasets);
    }

    private static object BuildPieChart(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var category = GetCategoricalColumnName(schema);
        var numeric = GetNumericColumnNames(schema).FirstOrDefault();
        if (category is null || numeric is null)
            return BuildMinimalConfig();

        var labels = results.Select(r => GetStringValue(r, category)).ToList();
        var data = results.Select(r => GetNumericValue(r, numeric) ?? 0d).ToList();

        return new
        {
            type = "doughnut",
            data = new
            {
                labels,
                datasets = new[]
                {
                    new { label = numeric, data }
                }
            },
            options = DefaultOptions()
        };
    }

    private static object BuildHeatMap(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        if (schema.Columns.Count < 2)
            return BuildMinimalConfig();

        var xName = schema.Columns[0].Name;
        var yName = schema.Columns[1].Name;
        var valueName = GetNumericColumnNames(schema).FirstOrDefault() ?? xName;

        var data = results.Select(r => new
        {
            x = GetStringValue(r, xName),
            y = GetStringValue(r, yName),
            v = GetNumericValue(r, valueName) ?? 0d
        }).ToList();

        return new
        {
            type = "matrix",
            data = new
            {
                datasets = new[]
                {
                    new { label = "Heatmap", data }
                }
            },
            options = DefaultOptions()
        };
    }

    private static object BuildScatter(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var numeric = GetNumericColumnNames(schema);
        if (numeric.Count < 2)
            return BuildMinimalConfig();

        var data = results.Select(r => new
        {
            x = GetNumericValue(r, numeric[0]) ?? 0d,
            y = GetNumericValue(r, numeric[1]) ?? 0d
        }).ToList();

        return new
        {
            type = "scatter",
            data = new
            {
                datasets = new[]
                {
                    new { label = $"{numeric[0]} vs {numeric[1]}", data }
                }
            },
            options = DefaultOptions()
        };
    }

    private static object BuildStat(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var numeric = GetNumericColumnNames(schema).FirstOrDefault();
        var value = numeric is null
            ? results.Count
            : results.Sum(r => GetNumericValue(r, numeric) ?? 0d);

        return new
        {
            type = "bar",
            data = new
            {
                labels = new[] { "value" },
                datasets = new[] { new { label = numeric ?? "count", data = new[] { value } } }
            },
            options = DefaultOptions()
        };
    }

    private static object BuildGauge(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var numeric = GetNumericColumnNames(schema).FirstOrDefault();
        if (numeric is null)
            return BuildMinimalConfig();

        var value = results.Sum(r => GetNumericValue(r, numeric) ?? 0d);
        var maxValue = value <= 0 ? 1 : value * 2;

        return new
        {
            type = "doughnut",
            data = new
            {
                labels = new[] { numeric, "remaining" },
                datasets = new[]
                {
                    new
                    {
                        data = new[] { value, maxValue - value },
                        backgroundColor = new[] { "#4F46E5", "#E5E7EB" }
                    }
                }
            },
            options = new
            {
                responsive = true,
                maintainAspectRatio = true,
                rotation = -90,
                circumference = 180
            }
        };
    }

    private static object BuildStackedAreaChart(QueryResultSchema schema, IList<Dictionary<string, object>> results)
    {
        var timeColumn = GetTimestampColumnName(schema);
        var categoryColumn = GetCategoricalColumnName(schema);
        var numeric = GetNumericColumnNames(schema).FirstOrDefault();
        if (timeColumn is null || categoryColumn is null || numeric is null)
            return BuildAreaChart(schema, results);

        var labels = results
            .Select(r => GetStringValue(r, timeColumn))
            .Distinct()
            .ToList();

        var grouped = results
            .GroupBy(r => GetStringValue(r, categoryColumn) ?? "unknown")
            .ToList();

        var datasets = grouped.Select(group => new
        {
            label = group.Key,
            data = labels.Select(label => GetGroupedValue(group, timeColumn, numeric, label)).ToList(),
            fill = true
        }).ToList();

        return new
        {
            type = "line",
            data = new { labels, datasets },
            options = new
            {
                responsive = true,
                maintainAspectRatio = true,
                scales = new
                {
                    x = new { stacked = true },
                    y = new { stacked = true }
                }
            }
        };
    }

    private static object BuildConfig(string type, IReadOnlyList<string?> labels, object datasets) =>
        new
        {
            type,
            data = new { labels, datasets },
            options = DefaultOptions()
        };

    private static object BuildMinimalConfig() =>
        new
        {
            type = "bar",
            data = new
            {
                labels = Array.Empty<string>(),
                datasets = Array.Empty<object>()
            },
            options = DefaultOptions()
        };

    private static object DefaultOptions() =>
        new
        {
            responsive = true,
            maintainAspectRatio = true
        };

    private static string? GetTimestampColumnName(QueryResultSchema schema) =>
        schema.Columns.FirstOrDefault(c =>
            c.IsTimestamp ||
            c.Name.Contains("time", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("date", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("bucket", StringComparison.OrdinalIgnoreCase))?.Name;

    private static string? GetCategoricalColumnName(QueryResultSchema schema) =>
        schema.Columns.FirstOrDefault(c => c.IsCategorical || (!c.IsNumeric && !c.IsTimestamp))?.Name;

    private static List<string> GetNumericColumnNames(QueryResultSchema schema) =>
        schema.Columns.Where(c => c.IsNumeric).Select(c => c.Name).ToList();

    private static string? GetStringValue(Dictionary<string, object> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value is null)
            return null;

        return value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetRawText(),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static double? GetNumericValue(Dictionary<string, object> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value is null)
            return null;

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.TryGetDouble(out var number) ? number : null,
                JsonValueKind.String => TryParseDouble(element.GetString()),
                _ => null
            };
        }

        return value switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            string s => TryParseDouble(s),
            _ => TryParseDouble(Convert.ToString(value, CultureInfo.InvariantCulture))
        };
    }

    private static double? TryParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static double GetGroupedValue(
        IGrouping<string, Dictionary<string, object>> group,
        string timeColumn,
        string numericColumn,
        string? label)
    {
        var match = group.FirstOrDefault(r =>
            string.Equals(GetStringValue(r, timeColumn), label, StringComparison.OrdinalIgnoreCase));
        return match is null ? 0d : GetNumericValue(match, numericColumn) ?? 0d;
    }
}
