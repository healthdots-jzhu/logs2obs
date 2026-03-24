using FluentAssertions;
using Logs2Obs.Adapters.Local.Options;
using Logs2Obs.Adapters.Local.QueryEngine;
using Logs2Obs.Core.Exceptions;
using Logs2Obs.Core.Models;
using Logs2Obs.Core.Query;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logs2Obs.Adapters.Local.Tests.QueryEngine;

using Options = Microsoft.Extensions.Options.Options;

public class DuckDbQueryEngineTests
{
    private readonly DuckDbQueryEngine _sut;

    public DuckDbQueryEngineTests()
    {
        var options   = Options.Create(new DuckDbOptions { DatabasePath = ":memory:", MaxQueryTimeoutSeconds = 30 });
        var validator = new SqlSafetyValidator();
        var logger    = NullLogger<DuckDbQueryEngine>.Instance;
        _sut = new DuckDbQueryEngine(options, logger, validator);
    }

    [Fact]
    public async Task SubmitAsync_ValidSelectQuery_ReturnsCompletedStatus()
    {
        // Arrange
        const string sql = "SELECT 1 AS result LIMIT 1";

        // Act
        var submitResult = await _sut.SubmitAsync("tenant-1", sql);

        // Assert
        submitResult.Should().NotBeNull();
        submitResult.Status.Should().Be(QueryStatus.Completed);
    }

    [Fact]
    public async Task SubmitAsync_DangerousSql_ThrowsSqlSafetyException()
    {
        // Arrange
        const string sql = "DROP TABLE logs";

        // Act
        var act = async () => await _sut.SubmitAsync("tenant-1", sql);

        // Assert
        await act.Should().ThrowAsync<SqlSafetyException>();
    }

    [Fact]
    public async Task EstimateCostAsync_AnyQuery_ReturnsEstimate()
    {
        // Arrange
        const string sql = "SELECT 1 LIMIT 1";

        // Act
        var estimate = await _sut.EstimateCostAsync(sql);

        // Assert
        estimate.Should().NotBeNull();
        estimate.EstimatedScanGb.Should().BeGreaterThanOrEqualTo(0);
        estimate.ConfidenceLevel.Should().NotBeNullOrEmpty();
    }
}
