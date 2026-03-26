namespace Logs2Obs.QueryEngine.Graphs;

using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Graphs;
using Logs2Obs.Core.Models;
using Microsoft.Extensions.Logging;

public sealed class GraphRenderService(
    GraphSuggestionEngine suggestionEngine,
    IAiService aiService,
    ILogger<GraphRenderService> logger)
{
    private readonly IAiService _aiService = aiService;
    private readonly ILogger<GraphRenderService> _logger = logger;

    public Task<GraphRenderResponse> RenderAsync(GraphRenderRequest request, CancellationToken ct = default)
    {
        _ = _aiService;
        _ = _logger;
        _ = suggestionEngine;
        _ = ct;

        var schema = request.Schema ?? new QueryResultSchema();
        var suggestions = GraphSuggestionEngine.SuggestFromSchema(schema);

        var selectedType = request.GraphType;
        if (request.AutoSelect && suggestions.Count > 0)
            selectedType = suggestions[0].GraphType;

        var vega = VegaLiteSpecBuilder.Build(selectedType, schema, request.Results);
        var chart = ChartJsConfigBuilder.Build(selectedType, schema, request.Results);
        var alternatives = suggestions
            .Select(s => s.GraphType.ToString())
            .Distinct()
            .Take(3)
            .ToList();

        var response = new GraphRenderResponse
        {
            Type = selectedType.ToString(),
            VegaLiteSpec = vega,
            ChartJsConfig = chart,
            AlternativeTypes = alternatives
        };

        return Task.FromResult(response);
    }
}
