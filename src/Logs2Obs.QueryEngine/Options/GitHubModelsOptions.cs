namespace Logs2Obs.QueryEngine.Options;

public sealed class GitHubModelsOptions
{
    public required string ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://models.inference.ai.azure.com";
    public string Model { get; set; } = "openai/gpt-4o";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxTokens { get; set; } = 1500;
}
