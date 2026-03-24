namespace Logs2Obs.Core.Graphs;

public sealed record ColumnInfo
{
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public bool IsNumeric { get; init; }
    public bool IsTimestamp { get; init; }
    public bool IsCategorical { get; init; }
}
