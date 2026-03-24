namespace Logs2Obs.Adapters.Local.Options;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ModelName { get; set; } = "llama3.2";
    public int MaxTokens { get; set; } = 2048;
    public int TimeoutSeconds { get; set; } = 60;
}
