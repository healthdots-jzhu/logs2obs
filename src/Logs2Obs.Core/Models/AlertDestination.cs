namespace Logs2Obs.Core.Models;

public sealed record AlertDestination
{
    public required string Type { get; init; }
    public string? WebhookUrl { get; init; }
    public string? IntegrationKey { get; init; }
}
