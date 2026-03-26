namespace Logs2Obs.QueryEngine.Graphs.Templates;

using Logs2Obs.Core.Graphs;
using Logs2Obs.Core.Models;

public sealed record PrebuiltGraphTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required GraphType GraphType { get; init; }
    public required string SqlTemplate { get; init; }
    public required object VegaLiteSpec { get; init; }
    public required object ChartJsConfig { get; init; }
    public required string RecommendedTimeRange { get; init; }
}

public static class PrebuiltGraphs
{
    private static readonly QueryResultSchema EmptySchema = new();
    private static readonly List<Dictionary<string, object>> EmptyResults = [];

    public static IReadOnlyList<PrebuiltGraphTemplate> All { get; } =
    [
        new PrebuiltGraphTemplate
        {
            Id = "error-rate-heatmap",
            Name = "Error Rate Heatmap",
            Description = "Error density by hour and day of week.",
            GraphType = GraphType.HeatMap,
            SqlTemplate = """
                SELECT hour_of_day, day_of_week, COUNT(*) AS error_count
                FROM logs
                WHERE level = 'error'
                  AND year = {year} AND month = {month} AND day BETWEEN {day_start} AND {day_end}
                GROUP BY hour_of_day, day_of_week
                LIMIT 500
                """,
            VegaLiteSpec = VegaLiteSpecBuilder.Build(GraphType.HeatMap, EmptySchema, EmptyResults),
            ChartJsConfig = ChartJsConfigBuilder.Build(GraphType.HeatMap, EmptySchema, EmptyResults),
            RecommendedTimeRange = "7d"
        },
        new PrebuiltGraphTemplate
        {
            Id = "latency-p99-trend",
            Name = "Latency P99 Trend",
            Description = "P99 latency per service over time.",
            GraphType = GraphType.LineChart,
            SqlTemplate = """
                SELECT timestamp, service, APPROX_QUANTILE(duration_ms, 0.99) AS p99_ms
                FROM logs
                WHERE duration_ms IS NOT NULL
                  AND year = {year} AND month = {month} AND day = {day}
                GROUP BY timestamp, service
                ORDER BY timestamp
                LIMIT 1000
                """,
            VegaLiteSpec = VegaLiteSpecBuilder.Build(GraphType.LineChart, EmptySchema, EmptyResults),
            ChartJsConfig = ChartJsConfigBuilder.Build(GraphType.LineChart, EmptySchema, EmptyResults),
            RecommendedTimeRange = "24h"
        },
        new PrebuiltGraphTemplate
        {
            Id = "error-rate-gauge",
            Name = "Error Rate Gauge",
            Description = "Current errors per minute.",
            GraphType = GraphType.Gauge,
            SqlTemplate = """
                SELECT COUNT(*) AS error_count
                FROM logs
                WHERE level = 'error'
                  AND year = {year} AND month = {month} AND day = {day} AND hour = {hour}
                LIMIT 1
                """,
            VegaLiteSpec = VegaLiteSpecBuilder.Build(GraphType.Gauge, EmptySchema, EmptyResults),
            ChartJsConfig = ChartJsConfigBuilder.Build(GraphType.Gauge, EmptySchema, EmptyResults),
            RecommendedTimeRange = "1h"
        },
        new PrebuiltGraphTemplate
        {
            Id = "top-errors-bar",
            Name = "Top Errors",
            Description = "Top 10 error messages.",
            GraphType = GraphType.BarChart,
            SqlTemplate = """
                SELECT message, COUNT(*) AS error_count
                FROM logs
                WHERE level = 'error'
                  AND year = {year} AND month = {month} AND day = {day}
                GROUP BY message
                ORDER BY error_count DESC
                LIMIT 10
                """,
            VegaLiteSpec = VegaLiteSpecBuilder.Build(GraphType.BarChart, EmptySchema, EmptyResults),
            ChartJsConfig = ChartJsConfigBuilder.Build(GraphType.BarChart, EmptySchema, EmptyResults),
            RecommendedTimeRange = "24h"
        },
        new PrebuiltGraphTemplate
        {
            Id = "status-code-donut",
            Name = "Status Code Distribution",
            Description = "HTTP status distribution.",
            GraphType = GraphType.PieChart,
            SqlTemplate = """
                SELECT status_code, COUNT(*) AS count
                FROM logs
                WHERE status_code IS NOT NULL
                  AND year = {year} AND month = {month} AND day = {day}
                GROUP BY status_code
                LIMIT 50
                """,
            VegaLiteSpec = VegaLiteSpecBuilder.Build(GraphType.PieChart, EmptySchema, EmptyResults),
            ChartJsConfig = ChartJsConfigBuilder.Build(GraphType.PieChart, EmptySchema, EmptyResults),
            RecommendedTimeRange = "24h"
        },
        new PrebuiltGraphTemplate
        {
            Id = "service-error-scatter",
            Name = "Service Error Scatter",
            Description = "Error count vs latency by service.",
            GraphType = GraphType.Scatter,
            SqlTemplate = """
                SELECT service, COUNT(*) AS error_count, AVG(duration_ms) AS avg_latency_ms
                FROM logs
                WHERE level = 'error'
                  AND duration_ms IS NOT NULL
                  AND year = {year} AND month = {month} AND day = {day}
                GROUP BY service
                LIMIT 200
                """,
            VegaLiteSpec = VegaLiteSpecBuilder.Build(GraphType.Scatter, EmptySchema, EmptyResults),
            ChartJsConfig = ChartJsConfigBuilder.Build(GraphType.Scatter, EmptySchema, EmptyResults),
            RecommendedTimeRange = "7d"
        },
        new PrebuiltGraphTemplate
        {
            Id = "log-volume-stacked",
            Name = "Log Volume by Type",
            Description = "Ingestion volume by log type over time.",
            GraphType = GraphType.StackedAreaChart,
            SqlTemplate = """
                SELECT timestamp, log_type, COUNT(*) AS count
                FROM logs
                WHERE year = {year} AND month = {month} AND day = {day}
                GROUP BY timestamp, log_type
                ORDER BY timestamp
                LIMIT 1000
                """,
            VegaLiteSpec = VegaLiteSpecBuilder.Build(GraphType.StackedAreaChart, EmptySchema, EmptyResults),
            ChartJsConfig = ChartJsConfigBuilder.Build(GraphType.StackedAreaChart, EmptySchema, EmptyResults),
            RecommendedTimeRange = "24h"
        },
        new PrebuiltGraphTemplate
        {
            Id = "alert-firing-timeline",
            Name = "Alert Firing Timeline",
            Description = "Alert firing events over time.",
            GraphType = GraphType.LineChart,
            SqlTemplate = """
                SELECT timestamp, COUNT(*) AS alert_count
                FROM logs
                WHERE category = 'alert'
                  AND year = {year} AND month = {month} AND day = {day}
                GROUP BY timestamp
                ORDER BY timestamp
                LIMIT 1000
                """,
            VegaLiteSpec = VegaLiteSpecBuilder.Build(GraphType.LineChart, EmptySchema, EmptyResults),
            ChartJsConfig = ChartJsConfigBuilder.Build(GraphType.LineChart, EmptySchema, EmptyResults),
            RecommendedTimeRange = "24h"
        }
    ];

    public static PrebuiltGraphTemplate? GetById(string id) =>
        All.FirstOrDefault(template => template.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
