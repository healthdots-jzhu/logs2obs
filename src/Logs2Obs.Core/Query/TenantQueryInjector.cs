namespace Logs2Obs.Core.Query;

using System.Text.RegularExpressions;
using Logs2Obs.Core.Exceptions;

/// <summary>
/// Injects tenant isolation filters into SQL queries as a defense-in-depth security measure.
/// Applied to every query before execution — cannot be bypassed by API callers.
/// </summary>
public static class TenantQueryInjector
{
    private static readonly Regex UnsafeChars = new(@"[';""\\-]{2,}|[';""\\]", RegexOptions.Compiled);

    /// <summary>
    /// Replaces the <c>{TENANT_FILTER}</c> placeholder in the given SQL with a safe tenant predicate.
    /// </summary>
    public static string InjectTenantFilter(string sql, string tenantId)
    {
        ValidateTenantId(tenantId);
        return sql.Replace("{TENANT_FILTER}", $"tenantid = '{tenantId}'");
    }

    /// <summary>Validates that the tenant ID does not contain unsafe characters that could enable SQL injection.</summary>
    /// <exception cref="QueryGuardException">Thrown if the tenant ID contains unsafe characters.</exception>
    public static void ValidateTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || UnsafeChars.IsMatch(tenantId))
            throw new QueryGuardException($"TenantId '{tenantId}' contains unsafe characters.");
    }
}
