using Microsoft.AspNetCore.Authentication;

namespace Logs2Obs.Api.Auth;

public sealed class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
    public string HeaderName { get; init; } = "X-Api-Key";
    public int CacheDurationSeconds { get; init; } = 300;
}
