namespace Logs2Obs.Core.Graphs;

using Logs2Obs.Core.Models;

/// <summary>Rule-based engine for suggesting appropriate graph types from a query result schema.</summary>
public class GraphSuggestionEngine
{
    /// <summary>
    /// Suggests graph types for the given query result schema using rule-based logic from Section 17.1.
    /// Returns suggestions ordered by confidence (highest first).
    /// </summary>
    public static IReadOnlyList<GraphSuggestion> SuggestFromSchema(QueryResultSchema schema)
    {
        List<GraphSuggestion> suggestions = [];

        bool hasTime      = schema.HasTimeColumn();
        bool hasCount     = schema.HasColumn("count") || schema.HasColumn("error_count") || schema.HasColumn("occurrences");
        bool hasCat       = schema.HasCategoricalColumn();
        bool hasCorrelation = schema.HasColumns("duration_ms", "request_bytes") ||
                              schema.HasColumns("p99_ms", "error_count");
        bool isSingleRow  = schema.RowCount == 1;
        bool isSingleNum  = schema.HasSingleNumericColumn();

        if (hasTime && hasCount)
            suggestions.Add(new GraphSuggestion { GraphType = GraphType.AreaChart, Confidence = 0.95, Reason = "Error/Count rate over time" });

        if (hasTime && !hasCount)
            suggestions.Add(new GraphSuggestion { GraphType = GraphType.LineChart, Confidence = 0.90, Reason = "Time series trend" });

        bool hasHourDay = schema.HasColumns("hour_of_day", "day_of_week");
        if (hasHourDay && hasCount)
            suggestions.Add(new GraphSuggestion { GraphType = GraphType.HeatMap, Confidence = 0.88, Reason = "Error density heatmap" });

        if (isSingleRow && isSingleNum)
            suggestions.Add(new GraphSuggestion { GraphType = GraphType.Gauge, Confidence = 0.92, Reason = "Single current value" });

        if (hasCat && hasCount && !hasTime)
            suggestions.Add(new GraphSuggestion { GraphType = GraphType.BarChart, Confidence = 0.80, Reason = "Category comparison" });

        if (hasCorrelation)
            suggestions.Add(new GraphSuggestion { GraphType = GraphType.Scatter, Confidence = 0.75, Reason = "Correlation analysis" });

        if (hasTime && hasCat)
            suggestions.Add(new GraphSuggestion { GraphType = GraphType.StackedAreaChart, Confidence = 0.70, Reason = "Stacked over time" });

        return [.. suggestions.OrderByDescending(s => s.Confidence)];
    }
}
