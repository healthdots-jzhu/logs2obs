namespace Logs2Obs.Core.Query;

using System.Globalization;
using System.Text.RegularExpressions;

public static class SqlParser
{
    private static readonly Regex IsoTimestampPattern = new(
        @"\d{4}-\d{2}-\d{2}(?:[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+\-]\d{2}:\d{2})?)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ParsedQuery Parse(string sql)
    {
        var (earliest, latest) = ExtractTimeRange(sql);
        var hasLimit = sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase);

        return new ParsedQuery
        {
            QueryId = Guid.CreateVersion7().ToString("N"),
            HasFullTextSearch = false,
            EarliestTimestamp = earliest,
            LatestTimestamp = latest,
            HasTimeFilter = earliest is not null && latest is not null,
            HasLimit = hasLimit
        };
    }

    private static (DateTimeOffset? Earliest, DateTimeOffset? Latest) ExtractTimeRange(string sql)
    {
        var segment = sql;
        var whereIndex = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        if (whereIndex >= 0)
            segment = sql[whereIndex..];

        var matches = IsoTimestampPattern.Matches(segment);
        if (matches.Count == 0)
            return (null, null);

        var values = new List<DateTimeOffset>(matches.Count);
        foreach (Match match in matches)
        {
            if (DateTimeOffset.TryParse(
                match.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            {
                values.Add(parsed);
            }
        }

        if (values.Count == 0)
            return (null, null);

        var earliest = values.Min();
        var latest = values.Max();
        return (earliest, latest);
    }
}
