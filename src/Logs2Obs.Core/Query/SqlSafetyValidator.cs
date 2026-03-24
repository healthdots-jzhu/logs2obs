namespace Logs2Obs.Core.Query;

using System.Text.RegularExpressions;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Exceptions;

/// <summary>Validates SQL queries to enforce read-only access and prevent dangerous operations.</summary>
public class SqlSafetyValidator : ISqlSafetyValidator
{
    private static readonly HashSet<string> ForbiddenKeywords =
        ["DROP", "DELETE", "INSERT", "UPDATE", "CREATE", "ALTER", "TRUNCATE", "GRANT", "REVOKE"];

    private static readonly Regex CrossJoinPattern =
        new(@"\bCROSS\s+JOIN\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc/>
    public void Validate(string sql)
    {
        var report = Analyze(sql);
        if (report.Errors.Count > 0)
            throw new SqlSafetyException(string.Join("; ", report.Errors));
    }

    /// <inheritdoc/>
    public SqlSafetyReport Analyze(string sql)
    {
        var errors   = new List<string>();
        var warnings = new List<string>();
        var upperSql = sql.ToUpperInvariant();

        foreach (var keyword in ForbiddenKeywords)
        {
            if (upperSql.Contains($" {keyword} ") || upperSql.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Forbidden keyword: {keyword}. Only SELECT statements are allowed.");
        }

        if (CrossJoinPattern.IsMatch(sql))
            warnings.Add("CROSS JOIN detected — may produce very large result sets.");

        if (!upperSql.Contains("YEAR") && !upperSql.Contains("MONTH") && !upperSql.Contains("DAY"))
            warnings.Add("No partition filter (year/month/day) detected — query may be expensive.");

        if (!upperSql.Contains("LIMIT"))
            warnings.Add("No LIMIT clause — result set may be very large.");

        return new SqlSafetyReport { Errors = errors, Warnings = warnings };
    }
}
