namespace Logs2Obs.Core.Models;

using Logs2Obs.Core.Graphs;

public sealed record GraphRenderRequest
{
    public required string QueryId { get; init; }
    public required string TenantId { get; init; }
    public GraphType GraphType { get; init; } = GraphType.LineChart;
    public bool AutoSelect { get; init; } = false;
    public IList<Dictionary<string, object>> Results { get; init; } = [];
    public QueryResultSchema? Schema { get; init; }
}
