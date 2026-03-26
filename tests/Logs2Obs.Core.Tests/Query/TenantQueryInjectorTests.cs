using Logs2Obs.Core.Query;

namespace Logs2Obs.Core.Tests.Query;

public class TenantQueryInjectorTests
{
    [Fact]
    public void Inject_AddsWhereClauseForTenant()
    {
        var sql = "select * from logs where {TENANT_FILTER}";

        var result = TenantQueryInjector.InjectTenantFilter(sql, "tenant-1");

        result.Should().Contain("tenantid = 'tenant-1'");
    }

    [Fact]
    public void Inject_WhenQueryAlreadyHasWhere_AppendsAndClause()
    {
        var sql = "select * from logs where level = 'Error' and {TENANT_FILTER}";

        var result = TenantQueryInjector.InjectTenantFilter(sql, "tenant-1");

        result.Should().Contain("level = 'Error' and tenantid = 'tenant-1'");
    }

    [Fact]
    public void Inject_WhenTenantIdIsEmpty_ThrowsArgumentException()
    {
        var act = () => TenantQueryInjector.InjectTenantFilter("select * from logs where {TENANT_FILTER}", string.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
