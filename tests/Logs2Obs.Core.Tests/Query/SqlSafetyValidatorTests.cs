using FluentAssertions;
using Logs2Obs.Core.Exceptions;
using Logs2Obs.Core.Query;

namespace Logs2Obs.Core.Tests.Query;

public class SqlSafetyValidatorTests
{
    private readonly SqlSafetyValidator _sut = new();

    [Theory]
    [InlineData("DROP TABLE logs")]
    [InlineData("DELETE FROM logs WHERE 1=1")]
    [InlineData("INSERT INTO logs VALUES ('x')")]
    [InlineData("UPDATE logs SET level='info'")]
    [InlineData("CREATE TABLE hack (id int)")]
    [InlineData("ALTER TABLE logs ADD COLUMN x int")]
    [InlineData("TRUNCATE TABLE logs")]
    [InlineData("GRANT ALL ON logs TO public")]
    [InlineData("REVOKE SELECT ON logs FROM user1")]
    public void Validate_WhenSqlContainsForbiddenKeyword_ThrowsSqlSafetyException(string sql)
    {
        var act = () => _sut.Validate(sql);
        act.Should().Throw<SqlSafetyException>();
    }

    [Fact]
    public void Analyze_WhenSqlContainsCrossJoin_ReturnsWarning()
    {
        var report = _sut.Analyze("SELECT * FROM logs CROSS JOIN other");
        report.Warnings.Should().Contain(w => w.Contains("CROSS JOIN"));
    }

    [Fact]
    public void Analyze_WhenSqlHasNoPartitionFilter_ReturnsWarning()
    {
        // No year/month/day column reference → warns about expensive scan
        var report = _sut.Analyze("SELECT * FROM logs WHERE level = 'Error' LIMIT 100");
        report.Warnings.Should().Contain(w => w.Contains("partition filter"));
    }

    [Fact]
    public void Analyze_WhenSqlHasNoLimit_ReturnsWarning()
    {
        var report = _sut.Analyze("SELECT * FROM logs WHERE year='2026'");
        report.Warnings.Should().Contain(w => w.Contains("LIMIT"));
    }

    [Fact]
    public void Validate_WhenValidSelectSql_DoesNotThrow()
    {
        const string sql = "SELECT sourceid, COUNT(*) FROM logs WHERE year='2026' AND month='03' GROUP BY sourceid LIMIT 100";
        var act = () => _sut.Validate(sql);
        act.Should().NotThrow();
    }

    [Fact]
    public void Analyze_WhenValidSql_ReturnsEmptyErrors()
    {
        var report = _sut.Analyze("SELECT * FROM logs WHERE year='2026' LIMIT 50");
        report.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_WhenSqlHasPartitionFilterAndLimit_ReturnsNoWarnings()
    {
        var report = _sut.Analyze("SELECT * FROM logs WHERE year='2026' AND month='03' AND day='01' LIMIT 100");
        report.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_WhenForbiddenKeyword_ReturnsErrors()
    {
        var report = _sut.Analyze("DROP TABLE logs");
        report.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("drop table logs")]
    [InlineData("Delete FROM logs WHERE 1=1")]
    [InlineData("insert into logs VALUES ('x')")]
    public void Validate_WhenForbiddenKeywordIsLowercase_ThrowsSqlSafetyException(string sql)
    {
        var act = () => _sut.Validate(sql);
        act.Should().Throw<SqlSafetyException>();
    }
}
