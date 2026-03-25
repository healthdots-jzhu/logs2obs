namespace Logs2Obs.QueryEngine.Tests.Validation;

public class SqlSafetyValidatorTests
{
    [Fact]
    public void Validate_WhenSelectOnly_DoesNotThrow()
    {
        var validator = new SqlSafetyValidator();

        Action action = () => validator.Validate("SELECT * FROM logs LIMIT 10");

        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_WhenContainsDrop_ThrowsSqlSafetyException()
    {
        var validator = new SqlSafetyValidator();

        Action action = () => validator.Validate("SELECT * FROM logs; DROP TABLE users;");

        action.Should().Throw<SqlSafetyException>();
    }

    [Fact]
    public void Validate_WhenContainsDelete_ThrowsSqlSafetyException()
    {
        var validator = new SqlSafetyValidator();

        Action action = () => validator.Validate("DELETE FROM logs;");

        action.Should().Throw<SqlSafetyException>();
    }

    [Fact]
    public void Validate_WhenContainsInsert_ThrowsSqlSafetyException()
    {
        var validator = new SqlSafetyValidator();

        Action action = () => validator.Validate("INSERT INTO logs VALUES (1);");

        action.Should().Throw<SqlSafetyException>();
    }

    [Fact]
    public void Validate_WhenContainsUpdate_ThrowsSqlSafetyException()
    {
        var validator = new SqlSafetyValidator();

        Action action = () => validator.Validate("UPDATE logs SET level = 'info';");

        action.Should().Throw<SqlSafetyException>();
    }

    [Fact]
    public void Analyze_WhenNoCrossJoin_HasNoWarnings()
    {
        var validator = new SqlSafetyValidator();

        var report = validator.Analyze("SELECT * FROM logs WHERE year = 2025 LIMIT 10");

        report.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_WhenMissingLimit_HasLimitWarning()
    {
        var validator = new SqlSafetyValidator();

        var report = validator.Analyze("SELECT * FROM logs WHERE year = 2025");

        report.Warnings.Should().Contain(w => w.Contains("No LIMIT clause"));
    }
}
