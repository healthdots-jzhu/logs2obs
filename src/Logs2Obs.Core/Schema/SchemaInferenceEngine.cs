namespace Logs2Obs.Core.Schema;

using Logs2Obs.Core.Models;

/// <summary>Infers schema field types from a batch of log entries by inspecting tag values.</summary>
public static class SchemaInferenceEngine
{
    /// <summary>
    /// Infers schema fields from the Tags dictionaries of the given log entries,
    /// detecting numeric, boolean, datetime, and string types.
    /// </summary>
    public static IReadOnlyList<SchemaField> InferSchema(IEnumerable<LogEntry> entries)
    {
        var fieldObservations = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (entry.Tags is null) continue;
            foreach (var (key, value) in entry.Tags)
            {
                if (!fieldObservations.TryGetValue(key, out var values))
                {
                    values = [];
                    fieldObservations[key] = values;
                }
                values.Add(value);
            }
        }

        return [.. fieldObservations.Select(kvp => new SchemaField
        {
            Name             = kvp.Key,
            InferredType     = InferType(kvp.Value),
            IsNullable       = true,
            ObservationCount = kvp.Value.Count
        })];
    }

    private static string InferType(List<string> values)
    {
        if (values.Count == 0) return "string";

        var nonEmpty = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (nonEmpty.Count == 0) return "string";

        if (nonEmpty.All(v => bool.TryParse(v, out _)))         return "bool";
        if (nonEmpty.All(v => long.TryParse(v, out _)))         return "int64";
        if (nonEmpty.All(v => double.TryParse(v, out _)))       return "double";
        if (nonEmpty.All(v => DateTimeOffset.TryParse(v, out _))) return "timestamp";

        return "string";
    }
}
