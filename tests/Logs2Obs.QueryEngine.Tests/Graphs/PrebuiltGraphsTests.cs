namespace Logs2Obs.QueryEngine.Tests.Graphs;

using Logs2Obs.QueryEngine.Graphs.Templates;

public class PrebuiltGraphsTests
{
    [Fact]
    public void All_ContainsEightTemplates()
    {
        PrebuiltGraphs.All.Should().HaveCount(8);
    }

    [Fact]
    public void GetById_WhenExists_ReturnsTemplate()
    {
        var result = PrebuiltGraphs.GetById("error-rate-heatmap");
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetById_WhenNotFound_ReturnsNull()
    {
        var result = PrebuiltGraphs.GetById("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public void AllTemplates_HaveSqlTemplate()
    {
        var templates = PrebuiltGraphs.All;

        foreach (var template in templates)
        {
            template.SqlTemplate.Should().NotBeNullOrWhiteSpace();
        }
    }
}
