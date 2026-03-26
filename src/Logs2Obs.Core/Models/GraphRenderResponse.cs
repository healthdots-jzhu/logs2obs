namespace Logs2Obs.Core.Models;

public sealed record GraphRenderResponse
{
    public required string Type { get; init; }
    public required object VegaLiteSpec { get; init; }
    public required object ChartJsConfig { get; init; }
    public IReadOnlyList<string> AlternativeTypes { get; init; } = [];
}
