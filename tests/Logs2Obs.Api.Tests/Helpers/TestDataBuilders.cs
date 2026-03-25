namespace Logs2Obs.Api.Tests.Helpers;

/// <summary>
/// Test data builders for API test scenarios.
/// Pattern follows Core test builders but focuses on API-specific models.
/// </summary>
public static class TestDataBuilders
{
    /// <summary>
    /// Generates a valid test API key with unique identifier.
    /// </summary>
    public static string AValidApiKey() => $"test-api-key-{Guid.NewGuid():N}";

    /// <summary>
    /// Generates a valid tenant ID with unique identifier.
    /// </summary>
    public static string AValidTenantId() => $"tenant-{Guid.NewGuid():N}";

    /// <summary>
    /// Creates a valid IngestLogsRequest with specified number of log entries.
    /// </summary>
    /// <param name="count">Number of log entries to include (default: 1)</param>
    public static IngestLogsRequest AValidIngestRequest(int count = 1)
    {
        var entries = new List<LogEntryDto>();
        for (int i = 0; i < count; i++)
        {
            entries.Add(AValidLogEntryDto());
        }

        return new IngestLogsRequest
        {
            Logs = entries
        };
    }

    /// <summary>
    /// Creates a valid LogEntryDto with all required fields populated.
    /// Uses realistic test data and current timestamp offset.
    /// </summary>
    public static LogEntryDto AValidLogEntryDto() => new()
    {
        SourceId = "test-service",
        LogType = "Application",
        Level = "Info", // Note: "Info" not "Information" per Bernard's Core implementation
        Environment = "test",
        Category = "unit-test",
        Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2),
        Message = "Test log entry for unit test"
    };
}

/// <summary>
/// Request model for log ingestion endpoint.
/// Matches expected API contract from design §9.
/// </summary>
public record IngestLogsRequest
{
    public required List<LogEntryDto> Logs { get; init; }
}
