namespace Logs2Obs.Core.Abstractions;

using Logs2Obs.Core.Exceptions;

/// <summary>Validates SQL queries for safety — enforces read-only, forbids destructive operations.</summary>
public interface ISqlSafetyValidator
{
    /// <summary>
    /// Validates the given SQL and throws <see cref="SqlSafetyException"/> if any violations are found.
    /// </summary>
    void Validate(string sql);

    /// <summary>Analyzes the given SQL and returns errors and warnings without throwing.</summary>
    SqlSafetyReport Analyze(string sql);
}
