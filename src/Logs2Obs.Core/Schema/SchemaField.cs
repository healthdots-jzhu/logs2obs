namespace Logs2Obs.Core.Schema;

public sealed record SchemaField
{
    public required string Name { get; init; }
    public required string InferredType { get; init; }
    public bool IsNullable { get; init; }
    public long ObservationCount { get; init; }
}
