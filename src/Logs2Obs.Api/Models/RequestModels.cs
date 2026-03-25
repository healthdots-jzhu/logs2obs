using Logs2Obs.Core.Graphs;
using Logs2Obs.Core.Models;

namespace Logs2Obs.Api.Models;

public sealed record IngestLogsRequest(IReadOnlyList<LogEntryDto> Entries);

public sealed record SearchRequest(string Query, string? Environment, int Limit = 50);

public sealed record SqlQueryRequest(string Sql, bool Async = true);

public sealed record NaturalLanguageRequest(string Question, string? Environment);

public sealed record GraphSuggestRequest(IReadOnlyList<ColumnInfo> Columns, int RowCount);

public sealed record SaveQueryRequest(string Name, string Sql, string? Description = null);

public sealed record RunSavedQueryRequest(string QueryId, Dictionary<string, string>? Parameters = null);

public sealed record CreatePullJobRequest(
    string Name,
    string SourceType,
    string Schedule,
    Dictionary<string, string> Configuration);

public sealed record UpdatePullJobRequest(
    string? Name = null,
    string? Schedule = null,
    Dictionary<string, string>? Configuration = null,
    bool? IsActive = null);

public sealed record CreateAlertRequest(
    string Name,
    string Query,
    string Condition,
    string Severity,
    IReadOnlyList<string> NotificationChannels);

public sealed record CreateApiKeyRequest(
    string Name,
    string? ExpiresAt = null,
    IReadOnlyList<string>? Permissions = null);

public sealed record StartReplayRequest(
    string QueryId,
    string StartTime,
    string EndTime,
    string? DestinationTopic = null);
